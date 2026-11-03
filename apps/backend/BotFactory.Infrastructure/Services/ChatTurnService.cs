using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BotFactory.Core;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Telemetry;
using BotFactory.Infrastructure.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotFactory.Infrastructure.Services;

public sealed class ChatTurnService : IChatTurnService
{
    private readonly BotFactoryDbContext _db;
    private readonly ITenantContextSetter _tenantSetter;
    private readonly IToolsetContextSetter _toolsetSetter;
    private readonly IToolsetContext _toolsetContext;
    private readonly GeminiChatService _geminiChat;
    private readonly IToolRegistry _toolRegistry;
    private readonly IOptions<GeminiChatOptions> _chatOptions;
    private readonly ITelemetryService _telemetry;
    private readonly IConversationStreamPublisher _streamPublisher;
    private readonly ILogger<ChatTurnService> _logger;

    public ChatTurnService(
        BotFactoryDbContext db,
        ITenantContextSetter tenantSetter,
        IToolsetContextSetter toolsetSetter,
        IToolsetContext toolsetContext,
        GeminiChatService geminiChat,
        IToolRegistry toolRegistry,
        IOptions<GeminiChatOptions> chatOptions,
        ITelemetryService telemetry,
        IConversationStreamPublisher streamPublisher,
        ILogger<ChatTurnService> logger)
    {
        _db = db;
        _tenantSetter = tenantSetter;
        _toolsetSetter = toolsetSetter;
        _toolsetContext = toolsetContext;
        _geminiChat = geminiChat;
        _toolRegistry = toolRegistry;
        _chatOptions = chatOptions;
        _telemetry = telemetry;
        _streamPublisher = streamPublisher;
        _logger = logger;
    }

    public async Task<ChatTurnResult> ExecuteAsync(
        ChatTurnExecuteRequest request,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken cancellationToken)
    {
        var conv = await _db.Conversations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken);

        if (conv is null)
            throw new InvalidOperationException($"conversation {request.ConversationId} not found");

        _tenantSetter.SetTenant(conv.OrganizationId);

        var bot = await _db.Bots.FindAsync([conv.BotId], cancellationToken);
        if (bot is null)
            throw new InvalidOperationException($"bot {conv.BotId} not found");

        // Load this bot's configured tools into the scoped toolset context so
        // ToolRegistry composes built-ins + the bot's enabled tools this turn.
        await _toolsetSetter.SetBotAsync(bot, cancellationToken);

        var history = await _db.Messages
            .Where(m => m.ConversationId == conv.Id && m.Role != MessageRole.Tool)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m => new GeminiChatMessage(m.Role, m.Content))
            .ToListAsync(cancellationToken);

        // A prior turn cancelled mid-flight (barge-in / a superseded turn)
        // persists its user message but no assistant reply — see the
        // `!wasCancelled` guard on the assistant save below — so history can end
        // on a dangling, unanswered user turn. Left in place it sits right before
        // the current question as a second consecutive user turn and biases the
        // model to answer the abandoned question. Drop the trailing user run; the
        // current question supersedes it.
        while (history.Count > 0 && history[^1].Role == MessageRole.User)
            history.RemoveAt(history.Count - 1);

        var userMessage = new Message
        {
            ConversationId = conv.Id,
            Role = MessageRole.User,
            Content = request.Text,
        };
        _db.Messages.Add(userMessage);
        await _db.SaveChangesAsync(cancellationToken);
        await _streamPublisher.PublishMessageAsync(userMessage, cancellationToken);

        var buffer = new StringBuilder();
        var sw = Stopwatch.StartNew();
        var provider = "gemini";
        var firstTokenEmitted = false;
        int? ttftMs = null;
        var segments = new List<object>();
        var extraContents = new List<GeminiExtraContent>();
        var maxToolInvokes = Math.Max(0, _chatOptions.Value.MaxToolIterations);
        var toolInvokes = 0;
        // The current user question. It must remain in `contents` on EVERY
        // Gemini pass this turn — including the post-tool answer pass — or the
        // model loses the question and answers the previous topic (observed as
        // "sigue con la anterior" after a tool call). Do not null this out; see
        // the note where the tool result is appended below.
        string? currentUserText = request.Text;
        var systemInstruction = ToolInstructionPrompt.Append(
            SpeechLanguagePrompt.BuildSystemInstruction(bot.MasterPrompt),
            _toolsetContext.EnabledTools);
        ChatTurnProviderError? providerError = null;
        var wasCancelled = false;

        await _telemetry.EmitAsync(new TelemetryEmit(
            "llm_started",
            EventSource.Backend,
            BotId: conv.BotId,
            ConversationId: conv.Id,
            UserId: conv.UserId,
            TurnId: request.TurnId,
            Stage: "llm",
            Provider: provider,
            Summary: $"prompt=\"{TruncateForSummary(request.Text, 80)}\" history={history.Count} msgs"),
            cancellationToken);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                GeminiFunctionCall? pendingCall = null;
                var geminiSw = Stopwatch.StartNew();
                int? geminiFirstDeltaMs = null;

                await foreach (var ev in _geminiChat.StreamAsync(
                                   systemInstruction,
                                   history,
                                   extraContents,
                                   currentUserText,
                                   cancellationToken))
                {
                    switch (ev)
                    {
                        case GeminiTextDelta d:
                            if (string.IsNullOrEmpty(d.Text))
                                continue;

                            var safe = d.Text.Replace("\r\n", " ", StringComparison.Ordinal)
                                .Replace('\n', ' ')
                                .Replace('\r', ' ');
                            geminiFirstDeltaMs ??= (int)geminiSw.ElapsedMilliseconds;
                            if (!firstTokenEmitted)
                            {
                                firstTokenEmitted = true;
                                ttftMs = (int)sw.ElapsedMilliseconds;
                                await _telemetry.EmitAsync(new TelemetryEmit(
                                    "llm_first_token",
                                    EventSource.Backend,
                                    BotId: conv.BotId,
                                    ConversationId: conv.Id,
                                    UserId: conv.UserId,
                                    TurnId: request.TurnId,
                                    Stage: "llm",
                                    LatencyMs: ttftMs,
                                    Provider: provider,
                                    Summary: $"after {toolInvokes} tool call(s)"),
                                    cancellationToken);
                            }

                            buffer.Append(safe);
                            await onDelta(safe, cancellationToken);
                            break;

                        case GeminiFunctionCall fc:
                            pendingCall = fc;
                            break;
                    }

                    if (pendingCall is not null)
                        break;
                }

                geminiSw.Stop();
                segments.Add(new
                {
                    type = "gemini",
                    ms = (int)geminiSw.ElapsedMilliseconds,
                    ttftMs = geminiFirstDeltaMs,
                });

                if (pendingCall is null)
                    break;

                if (toolInvokes >= maxToolInvokes)
                {
                    _logger.LogWarning(
                        "KB tool loop cap reached ({Max}) for conversation {ConversationId}",
                        maxToolInvokes,
                        conv.Id);

                    var capFallback = _chatOptions.Value.FallbackMessage;
                    if (buffer.Length == 0)
                    {
                        buffer.Append(capFallback);
                        try
                        {
                            await onDelta(capFallback, cancellationToken);
                        }
                        catch
                        {
                            /* best-effort */
                        }
                    }

                    provider = "gemini-fallback";
                    break;
                }

                toolInvokes++;

                var callContent =
                    "{\"name\":"
                    + JsonSerializer.Serialize(pendingCall.Name)
                    + ",\"args\":"
                    + pendingCall.Args.GetRawText()
                    + "}";

                _db.Messages.Add(new Message
                {
                    ConversationId = conv.Id,
                    Role = MessageRole.Tool,
                    Content = callContent,
                    Provider = ChatMessageProviders.GeminiTool,
                });
                await _db.SaveChangesAsync(cancellationToken);

                var handler = _toolRegistry.Get(pendingCall.Name)
                    ?? throw new InvalidOperationException(
                        $"No handler registered for tool '{pendingCall.Name}'.");

                var toolSw = Stopwatch.StartNew();
                var invocation = await handler.ExecuteAsync(
                    pendingCall.Args,
                    new ToolContext(bot, conv, request.TurnId),
                    cancellationToken);
                var toolLatencyMs = (int)toolSw.ElapsedMilliseconds;

                segments.Add(new
                {
                    type = "tool",
                    name = pendingCall.Name,
                    ms = toolLatencyMs,
                });

                var responseContent = JsonSerializer.Serialize(invocation.ResponsePayload);

                _db.Messages.Add(new Message
                {
                    ConversationId = conv.Id,
                    Role = MessageRole.Tool,
                    Content = responseContent,
                    Provider = ChatMessageProviders.GeminiTool,
                });
                await _db.SaveChangesAsync(cancellationToken);

                if (invocation.Telemetry is { } toolTelemetry)
                {
                    var hasDisplay = invocation.Display is not null;
                    var displayPayload = hasDisplay
                        ? new
                        {
                            name = handler.Name,
                            items = invocation.Display!
                                .Select(d => new { id = d.Id, label = d.Label, body = d.Body })
                                .ToArray(),
                        }
                        : null;

                    var mergedData = new
                    {
                        display = displayPayload,
                        payload = toolTelemetry.Data,
                    };

                    await _telemetry.EmitAsync(new TelemetryEmit(
                        toolTelemetry.EventName,
                        EventSource.Backend,
                        BotId: conv.BotId,
                        ConversationId: conv.Id,
                        UserId: conv.UserId,
                        TurnId: request.TurnId,
                        Stage: toolTelemetry.Stage,
                        LatencyMs: toolLatencyMs,
                        Provider: ChatMessageProviders.GeminiTool,
                        Summary: invocation.Summary,
                        Data: JsonSerializer.SerializeToElement(mergedData)),
                        cancellationToken);
                }

                // Grounded-only is a KB-retrieval behavior: an external tool
                // returning an empty result must NOT trigger the KB fallback.
                if (invocation.NoUsableResults
                    && pendingCall.Name == KbSearchToolHandler.ToolName
                    && !string.IsNullOrEmpty(bot.GroundedOnlyMessage))
                {
                    var msg = bot.GroundedOnlyMessage!;
                    buffer.Clear();
                    buffer.Append(msg);
                    try
                    {
                        await onDelta(msg, cancellationToken);
                    }
                    catch
                    {
                        /* best-effort */
                    }

                    provider = ChatMessageProviders.GeminiGroundedOnly;
                    break;
                }

                var modelParts = new object[]
                {
                    new { functionCall = new { name = pendingCall.Name, args = pendingCall.Args } },
                };
                var userParts = new object[]
                {
                    new
                    {
                        functionResponse = new
                        {
                            name = pendingCall.Name,
                            // The requested args ride alongside the result so the
                            // model grounds its answer on what was actually asked
                            // (observed: correct tool results answered off-topic
                            // when the result stood alone).
                            response = new
                            {
                                solicitud = pendingCall.Args,
                                resultado = invocation.ResponsePayload,
                            },
                        },
                    },
                };

                extraContents.Add(new GeminiExtraContent("model", modelParts));
                extraContents.Add(new GeminiExtraContent("user", userParts));
                // Keep currentUserText for the next pass. BuildStreamRequest
                // places it before extraContents, so the answer pass sends
                // [history, user:question, model:functionCall, user:functionResponse]
                // and the model still sees the actual question. A previous
                // `currentUserText = null;` here dropped the question from the
                // post-tool pass and made the bot answer the previous topic
                // (regression of commit e42fff8); do not reintroduce it.
            }

            if (buffer.Length == 0 || string.IsNullOrWhiteSpace(buffer.ToString()))
            {
                // The model ended the turn with no text at all (observed after
                // tool-error payloads) or with whitespace only. Never leave the
                // user with a silent, empty answer — stream the fallback instead.
                var emptyFallback = _chatOptions.Value.FallbackMessage;
                buffer.Clear();
                buffer.Append(emptyFallback);
                try
                {
                    await onDelta(emptyFallback, cancellationToken);
                }
                catch
                {
                    /* best-effort */
                }

                provider = "gemini-fallback";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            wasCancelled = true;
            provider = "gemini-partial";
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini chat failed for conversation {ConversationId}", conv.Id);

            ProviderErrorInfo? errInfo = ex is ProviderServiceException pse
                ? pse.Info
                : null;

            if (errInfo is not null)
            {
                providerError = new ChatTurnProviderError(
                    errInfo.ErrorCode,
                    errInfo.UserMessage,
                    errInfo.OperatorMessage);
            }

            if (buffer.Length == 0 || string.IsNullOrWhiteSpace(buffer.ToString()))
            {
                var fallback = _chatOptions.Value.FallbackMessage;
                buffer.Clear();
                buffer.Append(fallback);
                try
                {
                    await onDelta(fallback, CancellationToken.None);
                }
                catch
                {
                    /* best-effort */
                }
            }

            provider = "gemini-fallback";

            var summary = errInfo?.OperatorMessage
                ?? $"failed after {toolInvokes} tool call(s)";

            JsonElement? errorData = errInfo is not null
                ? JsonSerializer.SerializeToElement(new { errorCode = errInfo.ErrorCode })
                : null;

            await _telemetry.EmitAsync(new TelemetryEmit(
                "llm_error",
                EventSource.Backend,
                BotId: conv.BotId,
                ConversationId: conv.Id,
                UserId: conv.UserId,
                TurnId: request.TurnId,
                Stage: "llm",
                Provider: provider,
                ErrorMessage: ex.Message,
                Summary: summary,
                Data: errorData),
                CancellationToken.None);
        }
        finally
        {
            sw.Stop();
            var totalMs = (int)sw.ElapsedMilliseconds;

            if (!wasCancelled)
            {
                var assistantMessage = new Message
                {
                    ConversationId = conv.Id,
                    Role = MessageRole.Assistant,
                    Content = buffer.ToString(),
                    Provider = provider,
                    LatencyMs = totalMs,
                };
                _db.Messages.Add(assistantMessage);
                await _db.SaveChangesAsync(CancellationToken.None);
                await _streamPublisher.PublishMessageAsync(assistantMessage, CancellationToken.None);

                var turnData = JsonSerializer.SerializeToElement(new
                {
                    turnId = request.TurnId,
                    ttftMs,
                    totalMs,
                    outcome = MapTurnOutcome(provider),
                    toolCount = toolInvokes,
                    charCount = buffer.Length,
                    segments,
                });

                await _telemetry.EmitAsync(new TelemetryEmit(
                    "backend_completed",
                    EventSource.Backend,
                    BotId: conv.BotId,
                    ConversationId: conv.Id,
                    UserId: conv.UserId,
                    TurnId: request.TurnId,
                    Stage: "llm",
                    LatencyMs: totalMs,
                    Provider: provider,
                    Summary: $"chars={buffer.Length} tools={toolInvokes} ttft={ttftMs}ms",
                    Data: turnData),
                    CancellationToken.None);
            }
            else
            {
                await _telemetry.EmitAsync(new TelemetryEmit(
                    "turn_cancelled",
                    EventSource.Backend,
                    BotId: conv.BotId,
                    ConversationId: conv.Id,
                    UserId: conv.UserId,
                    TurnId: request.TurnId,
                    Stage: "llm",
                    Provider: provider,
                    Summary: $"cancelled after {buffer.Length} chars"),
                    CancellationToken.None);
            }
        }

        return new ChatTurnResult(
            buffer.ToString(),
            provider,
            ttftMs,
            (int)sw.ElapsedMilliseconds,
            toolInvokes,
            segments,
            providerError,
            wasCancelled);
    }

    private static string MapTurnOutcome(string provider) => provider switch
    {
        "gemini" => "ok",
        "gemini-partial" => "partial",
        "gemini-fallback" => "fallback",
        ChatMessageProviders.GeminiGroundedOnly => "grounded-only",
        _ => provider,
    };

    private static string TruncateForSummary(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var collapsed = value.Replace('\n', ' ').Replace('\r', ' ');
        return collapsed.Length <= max ? collapsed : collapsed[..max] + "…";
    }
}
