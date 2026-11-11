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

        // «Respaldo obligatorio» bots also get the lookups behind their recent answers,
        // and those results (in full) become evidence for the grounding gate.
        List<GeminiChatMessage> history;
        List<string> historyEvidence = [];
        if (bot.RequireGrounding)
        {
            (history, historyEvidence) = await LoadHistoryWithEvidenceAsync(conv.Id, cancellationToken);
        }
        else
        {
            history = await _db.Messages
                .Where(m => m.ConversationId == conv.Id && m.Role != MessageRole.Tool)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .Select(m => new GeminiChatMessage(m.Role, m.Content, null))
                .ToListAsync(cancellationToken);
        }

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
        var toolCapReached = false;
        var continued = false;
        // «Respaldo obligatorio»: Google refused the forced lookup and the call was repeated once
        // without forcing it (see where a call gives no answer, below).
        var unforcedRetry = false;
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

        // «Respaldo obligatorio»: the answer reaches the user only through the gate,
        // which checks every datum against the bot's sources.
        GroundingGate? gate = null;
        if (bot.RequireGrounding)
        {
            gate = new GroundingGate(string.IsNullOrWhiteSpace(bot.GroundedOnlyMessage)
                ? DefaultUngroundedNote
                : bot.GroundedOnlyMessage!);
            gate.AddEvidence(systemInstruction);
            gate.AddEvidence(request.Text);
            foreach (var m in history.Where(h => h.Role == MessageRole.User))
                gate.AddEvidence(m.Content);
            foreach (var e in historyEvidence)
                gate.AddEvidence(e);
        }

        async Task EmitAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

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

            buffer.Append(text);
            await onDelta(text, cancellationToken);
        }

        // Token accounting. Gemini reports usageMetadata on every chunk of a call with
        // running counts, so the last report of the call wins. `geminiCallOpen` marks a
        // call whose usage has not been recorded yet: the finally block uses it to
        // still record a call cut short by barge-in or an error, since Gemini bills
        // the tokens it had already counted.
        GeminiUsage? passUsage = null;
        // How Google ended the call in flight: its finishReason (with finishMessage and the safety
        // categories it flagged) or, for a request it refused outright, promptFeedback.blockReason.
        GeminiFinish? passFinish = null;
        GeminiPromptBlocked? passBlock = null;
        var geminiCallOpen = false;
        var geminiCalls = 0;
        var usageTotals = new TurnUsageTotals();

        // Records the call in flight: one `llm_usage` telemetry event, one log line
        // (it shows in Registros) and the turn totals. Best-effort on purpose — an
        // observability failure must never turn a good answer into the fallback.
        async Task<GeminiUsage?> RecordGeminiCallAsync(bool completed, int? ms)
        {
            if (!geminiCallOpen)
                return null;
            geminiCallOpen = false;

            var usage = passUsage;
            passUsage = null;

            // A call that failed before Gemini counted anything has nothing to record;
            // llm_error already covers it.
            if (!completed && usage is null)
                return null;

            geminiCalls++;
            usageTotals.Add(usage);

            try
            {
                if (usage is null)
                {
                    _logger.LogWarning(
                        "Gemini reported no usageMetadata conversation={ConversationId} turn={TurnId} call={Call}",
                        conv.Id, request.TurnId, geminiCalls);
                }
                else
                {
                    _logger.LogInformation(
                        "Gemini usage conversation={ConversationId} turn={TurnId} call={Call} prompt={Prompt} cached={Cached} output={Output} thoughts={Thoughts} total={Total}",
                        conv.Id, request.TurnId, geminiCalls,
                        usage.PromptTokens, usage.CachedTokens, usage.OutputTokens, usage.ThoughtsTokens, usage.TotalTokens);
                }

                await _telemetry.EmitAsync(new TelemetryEmit(
                    "llm_usage",
                    EventSource.Backend,
                    BotId: conv.BotId,
                    ConversationId: conv.Id,
                    UserId: conv.UserId,
                    TurnId: request.TurnId,
                    Stage: "llm",
                    LatencyMs: ms,
                    Provider: "gemini",
                    Summary: (usage is null ? "usageMetadata not reported" : usage.Describe()) + UnusualEnding(),
                    Data: JsonSerializer.SerializeToElement(new
                    {
                        model = _chatOptions.Value.ChatModel,
                        call = geminiCalls,
                        reported = usage is not null,
                        complete = completed,
                        finishReason = passFinish?.Reason,
                        blockReason = passBlock?.Reason,
                        promptTokens = usage?.PromptTokens,
                        outputTokens = usage?.OutputTokens,
                        cachedTokens = usage?.CachedTokens,
                        thoughtsTokens = usage?.ThoughtsTokens,
                        totalTokens = usage?.TotalTokens,
                        cachedPercent = usage?.CachedPercent,
                    })),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not record Gemini usage for conversation {ConversationId}", conv.Id);
            }

            return usage;
        }

        // The call's ending for the llm_usage line, only when it is not an ordinary one (STOP).
        string UnusualEnding()
        {
            var ending = "";
            if (passFinish is not null && passFinish.Reason != "STOP")
                ending += $" finish={passFinish.Reason}";
            if (passBlock is not null)
                ending += $" blocked={passBlock.Reason}";
            return ending;
        }

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
            // Tool policy for the next Gemini call: the model decides (AUTO) unless the
            // turn needs a text-only answer — after the tool budget ran out, or to
            // continue an answer cut by the output limit — which forbids tools (NONE).
            // «Respaldo obligatorio»: the first call of the turn must consult a source (ANY).
            var toolChoice = bot.RequireGrounding ? ToolChoice.Required : ToolChoice.Auto;
            double? temperature = bot.RequireGrounding ? _chatOptions.Value.GroundedTemperature : null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var forcedPass = toolChoice == ToolChoice.Required;

                var calls = new List<GeminiFunctionCall>();
                var passText = new StringBuilder();
                string? finishReason = null;
                var geminiSw = Stopwatch.StartNew();
                int? geminiFirstDeltaMs = null;

                // One retry for a transient failure (429, 5xx, timeout, network drop),
                // and only while this call has streamed no text: retrying after that
                // would repeat words the user already received.
                for (var attempt = 1; ; attempt++)
                {
                    passUsage = null;
                    passFinish = null;
                    passBlock = null;
                    geminiCallOpen = true;
                    calls.Clear();
                    finishReason = null;

                    try
                    {
                        // The whole stream is read, not just up to the first function
                        // call: the model may ask for several tools in one answer, and
                        // the usage and finish reason close the stream.
                        await foreach (var ev in _geminiChat.StreamAsync(
                                           systemInstruction,
                                           history,
                                           extraContents,
                                           currentUserText,
                                           cancellationToken,
                                           toolChoice,
                                           temperature))
                        {
                            switch (ev)
                            {
                                case GeminiTextDelta d:
                                    if (string.IsNullOrEmpty(d.Text))
                                        continue;

                                    // Line breaks are kept: they are what turns a list into a
                                    // list in the chat. Only the voice channel flattens them
                                    // (LiveKitEndpoints), since SSE frames and TTS need one line.
                                    var safe = d.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
                                        .Replace('\r', '\n');
                                    geminiFirstDeltaMs ??= (int)geminiSw.ElapsedMilliseconds;
                                    passText.Append(safe);
                                    if (gate is null)
                                    {
                                        await EmitAsync(safe);
                                    }
                                    else
                                    {
                                        foreach (var ready in gate.Push(safe))
                                            await EmitAsync(ready);
                                    }

                                    break;

                                case GeminiFunctionCall fc:
                                    calls.Add(fc);
                                    break;

                                case GeminiUsage u:
                                    passUsage = u;
                                    break;

                                case GeminiFinish f:
                                    finishReason = f.Reason;
                                    passFinish = f;
                                    break;

                                case GeminiPromptBlocked b:
                                    passBlock = b;
                                    break;
                            }
                        }

                        break;
                    }
                    catch (Exception ex) when (attempt == 1
                                               && passText.Length == 0
                                               && IsTransientProviderFailure(ex, cancellationToken))
                    {
                        // Nothing billable to record for the failed attempt.
                        geminiCallOpen = false;
                        passUsage = null;

                        var delayMs = RetryDelayMs();
                        _logger.LogWarning(
                            ex,
                            "Gemini call failed transiently; retrying once in {DelayMs} ms (conversation {ConversationId})",
                            delayMs,
                            conv.Id);
                        await EmitBestEffortAsync(new TelemetryEmit(
                            "llm_retry",
                            EventSource.Backend,
                            BotId: conv.BotId,
                            ConversationId: conv.Id,
                            UserId: conv.UserId,
                            TurnId: request.TurnId,
                            Stage: "llm",
                            Provider: "gemini",
                            Summary: $"{DescribeFailure(ex)}; retrying in {delayMs} ms"));

                        if (delayMs > 0)
                            await Task.Delay(delayMs, cancellationToken);
                        geminiSw.Restart();
                        geminiFirstDeltaMs = null;
                    }
                }

                geminiSw.Stop();
                var callUsage = await RecordGeminiCallAsync(
                    completed: true,
                    ms: (int)geminiSw.ElapsedMilliseconds);
                segments.Add(new
                {
                    type = "gemini",
                    ms = (int)geminiSw.ElapsedMilliseconds,
                    ttftMs = geminiFirstDeltaMs,
                    tokens = callUsage is null
                        ? null
                        : new
                        {
                            promptTokens = callUsage.PromptTokens,
                            outputTokens = callUsage.OutputTokens,
                            cachedTokens = callUsage.CachedTokens,
                            thoughtsTokens = callUsage.ThoughtsTokens,
                        },
                });

                // Gemini ended the call with no text and no lookup it could act on: the visitor will
                // get the error message, and what Google said about why is the only trace of the
                // cause. Recorded for every bot, whatever the reason.
                if (string.IsNullOrWhiteSpace(passText.ToString())
                    && (calls.Count == 0 || toolChoice == ToolChoice.None))
                {
                    // «Respaldo obligatorio»: Google refuses to force a lookup on some content
                    // (observed: a child-abuse question, PROHIBITED_CONTENT, only with the lookup
                    // forced — the same bot answers it when the call is not forced). That call is
                    // repeated once with the lookup left to the model. Nothing else is relaxed: the
                    // model has the same prompt, knowledge base and tools, may still consult them,
                    // and the gate keeps every datum that is in no source out of the answer.
                    var retryUnforced = forcedPass
                                        && calls.Count == 0
                                        && !unforcedRetry
                                        && GeminiSafety.IsRefusal(passFinish?.Reason, passBlock?.Reason);
                    var why = DescribeNoAnswer(passFinish, passBlock, toolChoice, calls.Count)
                              + (retryUnforced ? " · se repite sin consulta obligatoria" : "");
                    _logger.LogWarning(
                        "Gemini gave no answer for conversation {ConversationId}: {Why}",
                        conv.Id,
                        why);
                    await EmitBestEffortAsync(new TelemetryEmit(
                        "llm_no_answer",
                        EventSource.Backend,
                        BotId: conv.BotId,
                        ConversationId: conv.Id,
                        UserId: conv.UserId,
                        TurnId: request.TurnId,
                        Stage: "llm",
                        Provider: "gemini",
                        Summary: why,
                        Data: JsonSerializer.SerializeToElement(new
                        {
                            call = geminiCalls,
                            finishReason = passFinish?.Reason,
                            finishMessage = passFinish?.Message,
                            blockReason = passBlock?.Reason,
                            safetyRatings = passBlock?.SafetyRatings ?? passFinish?.SafetyRatings,
                            toolMode = ToolModeName(toolChoice),
                            ignoredToolCalls = calls.Count,
                            outputTokens = callUsage?.OutputTokens,
                            retryUnforced,
                        })));

                    if (retryUnforced)
                    {
                        unforcedRetry = true;
                        toolChoice = ToolChoice.Auto;
                        continue;
                    }
                }

                // A text answer ends the turn. So does any answer to a pass that forbade
                // tools: a model that still asks for one gets nothing more, which also
                // keeps the loop finite.
                if (calls.Count == 0 || toolChoice == ToolChoice.None)
                {
                    if (string.Equals(finishReason, "MAX_TOKENS", StringComparison.Ordinal)
                        && passText.Length > 0)
                    {
                        if (!continued)
                        {
                            // Cut at the output limit, possibly in the middle of a phone
                            // number or a link: what was streamed stays, and one more pass
                            // finishes it. A half datum is a wrong datum.
                            continued = true;
                            _logger.LogWarning(
                                "Answer cut at the output limit for conversation {ConversationId}; asking Gemini to continue",
                                conv.Id);
                            await EmitBestEffortAsync(new TelemetryEmit(
                                "llm_truncated",
                                EventSource.Backend,
                                BotId: conv.BotId,
                                ConversationId: conv.Id,
                                UserId: conv.UserId,
                                TurnId: request.TurnId,
                                Stage: "llm",
                                Provider: "gemini",
                                Summary: $"cut at the output limit after {passText.Length} chars; continuing"));

                            extraContents.Add(new GeminiExtraContent("model", [new { text = passText.ToString() }]));
                            extraContents.Add(new GeminiExtraContent("user", [new { text = ContinuationNote }]));
                            toolChoice = ToolChoice.None;
                            continue;
                        }

                        _logger.LogWarning(
                            "Answer still cut at the output limit after one continuation for conversation {ConversationId}",
                            conv.Id);
                        await EmitBestEffortAsync(new TelemetryEmit(
                            "llm_truncated",
                            EventSource.Backend,
                            BotId: conv.BotId,
                            ConversationId: conv.Id,
                            UserId: conv.UserId,
                            TurnId: request.TurnId,
                            Stage: "llm",
                            Provider: "gemini",
                            Summary: "still cut after one continuation"));
                    }

                    break;
                }

                // Every call of this answer runs now, within the turn's budget. They run
                // one after another: the handlers share this request's DbContext.
                var remaining = Math.Max(0, maxToolInvokes - toolInvokes);
                var toRun = calls.Take(remaining).ToList();
                var notRun = calls.Skip(toRun.Count).Select(c => c.Name).ToList();
                var modelParts = new List<object>();
                var userParts = new List<object>();
                var allKbEmpty = toRun.Count > 0;

                foreach (var call in toRun)
                {
                    toolInvokes++;
                    var outcome = await ExecuteToolCallAsync(call, bot, conv, request.TurnId, segments, cancellationToken);
                    gate?.AddEvidence(EvidenceJson(outcome.Payload));
                    if (!outcome.KbWithoutResults)
                        allKbEmpty = false;

                    modelParts.Add(new { functionCall = new { name = call.Name, args = call.Args } });
                    userParts.Add(new
                    {
                        functionResponse = new
                        {
                            name = call.Name,
                            // The requested args ride alongside the result so the
                            // model grounds its answer on what was actually asked
                            // (observed: correct tool results answered off-topic
                            // when the result stood alone).
                            response = new
                            {
                                solicitud = call.Args,
                                resultado = outcome.Payload,
                            },
                        },
                    });
                }

                // Grounded-only is a KB-retrieval behavior: it ends the turn only when
                // everything consulted was the knowledge base and it found nothing. An
                // external tool returning an empty result must NOT trigger it.
                // Not after a forced lookup: there the model had to call something, and an
                // empty search for "gracias" must not become "no tengo esa información".
                // The gate still keeps any invented datum out of the answer.
                if (allKbEmpty && notRun.Count == 0 && !string.IsNullOrEmpty(bot.GroundedOnlyMessage) && !forcedPass)
                {
                    gate?.Discard();
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

                // Only the first call of the turn is forced; after the lookup the model is free.
                if (toolChoice == ToolChoice.Required)
                    toolChoice = ToolChoice.Auto;

                if (toRun.Count > 0)
                {
                    extraContents.Add(new GeminiExtraContent("model", modelParts.ToArray()));
                    extraContents.Add(new GeminiExtraContent("user", userParts.ToArray()));
                }

                if (notRun.Count > 0)
                {
                    // Budget exhausted: instead of failing the whole turn, one last pass
                    // with tools forbidden answers with what was gathered and says what
                    // could not be consulted.
                    toolCapReached = true;
                    _logger.LogWarning(
                        "Tool cap reached ({Max}) for conversation {ConversationId}; answering with what was gathered",
                        maxToolInvokes,
                        conv.Id);
                    await EmitBestEffortAsync(new TelemetryEmit(
                        "tool_cap_reached",
                        EventSource.Backend,
                        BotId: conv.BotId,
                        ConversationId: conv.Id,
                        UserId: conv.UserId,
                        TurnId: request.TurnId,
                        Stage: "llm",
                        Provider: "gemini",
                        Summary: $"cap={maxToolInvokes}; not consulted: {string.Join(", ", notRun)}"));

                    AppendNoteToLastUserContent(extraContents, CapNote(notRun));
                    toolChoice = ToolChoice.None;
                }

                // Keep currentUserText for the next pass. BuildStreamRequest
                // places it before extraContents, so the answer pass sends
                // [history, user:question, model:functionCall, user:functionResponse]
                // and the model still sees the actual question. A previous
                // `currentUserText = null;` here dropped the question from the
                // post-tool pass and made the bot answer the previous topic
                // (regression of commit e42fff8); do not reintroduce it.
            }

            if (gate is not null)
            {
                foreach (var ready in gate.Flush())
                    await EmitAsync(ready);

                foreach (var blocked in gate.Blocked)
                {
                    _logger.LogWarning(
                        "Grounding gate dropped an unsupported {Kind} for conversation {ConversationId}",
                        blocked.Kind,
                        conv.Id);
                    await EmitBestEffortAsync(new TelemetryEmit(
                        "grounding_blocked",
                        EventSource.Backend,
                        BotId: conv.BotId,
                        ConversationId: conv.Id,
                        UserId: conv.UserId,
                        TurnId: request.TurnId,
                        Stage: "llm",
                        Provider: "gemini",
                        Summary: $"{blocked.Kind} '{blocked.Value}' is in no source; sentence not delivered",
                        Data: JsonSerializer.SerializeToElement(new { kind = blocked.Kind, value = blocked.Value, sentence = blocked.Sentence })));
                }

                if (toolInvokes == 0)
                {
                    await EmitBestEffortAsync(new TelemetryEmit(
                        "turn_without_tool",
                        EventSource.Backend,
                        BotId: conv.BotId,
                        ConversationId: conv.Id,
                        UserId: conv.UserId,
                        TurnId: request.TurnId,
                        Stage: "llm",
                        Provider: "gemini",
                        Summary: "answered without consulting any source"));
                }
            }

            if (buffer.Length == 0 || string.IsNullOrWhiteSpace(buffer.ToString()))
            {
                // The model ended the turn with no text at all (observed after
                // tool-error payloads) or with whitespace only. Never leave the
                // user with a silent, empty answer — stream the fallback instead.
                var emptyFallback = BotErrorMessages.Resolve(bot.ErrorMessage, _chatOptions.Value.FallbackMessage);
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

            // What the visitor reads for this failure: the bot's own words when it has them
            // (one for a spent quota, one for any other error), the platform's otherwise. The same
            // text is the answer in the chat, what a voice call speaks and the error event's userMessage.
            var isQuota = errInfo?.ErrorCode == ProviderErrorClassifier.CodeGeminiQuotaExceeded;
            if (errInfo is not null)
            {
                providerError = new ChatTurnProviderError(
                    errInfo.ErrorCode,
                    BotErrorMessages.Resolve(isQuota ? bot.QuotaMessage : bot.ErrorMessage, errInfo.UserMessage),
                    errInfo.OperatorMessage);
            }

            if (buffer.Length == 0 || string.IsNullOrWhiteSpace(buffer.ToString()))
            {
                // A spent quota gets its own words (what is happening, what to do) instead of
                // the generic «no pude procesar eso»; the same text is spoken in a voice call.
                var fallback = isQuota
                    ? providerError!.UserMessage
                    : BotErrorMessages.Resolve(bot.ErrorMessage, _chatOptions.Value.FallbackMessage);
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

            var retryAfterSeconds = (ex as ProviderServiceException)?.RetryAfter is { } asked
                ? (int?)Math.Ceiling(asked.TotalSeconds)
                : null;
            var summary = errInfo is null
                ? $"failed after {toolInvokes} tool call(s)"
                : retryAfterSeconds is { } secs
                    ? $"{errInfo.OperatorMessage} (Google pide esperar {secs} s)"
                    : errInfo.OperatorMessage;

            JsonElement? errorData = errInfo is not null
                ? JsonSerializer.SerializeToElement(new { errorCode = errInfo.ErrorCode, retryAfterSeconds })
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

            // No-op unless a Gemini call was cut short (barge-in, error) after Gemini
            // had already counted some tokens.
            await RecordGeminiCallAsync(completed: false, ms: null);

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
                    tokens = usageTotals.ToData(),
                    toolCapReached,
                    continued,
                    unforcedRetry,
                    groundingBlocked = gate?.Blocked.Count ?? 0,
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
                    Summary: $"chars={buffer.Length} tools={toolInvokes} ttft={ttftMs}ms in={usageTotals.PromptTokens} cached={usageTotals.CachedTokens} out={usageTotals.OutputTokens}",
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

    // Notes the platform adds to the conversation for one pass. They speak to the
    // model, in the bot's language, and never reach the user.
    /// <summary>Closing note when a «Respaldo obligatorio» bot has no grounded-only message configured.</summary>
    private const string DefaultUngroundedNote = "No tengo ese dato confirmado en mis fuentes.";

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        // Keep accents and "&" readable: the gate compares text, not escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string EvidenceJson(object payload) => JsonSerializer.Serialize(payload, EvidenceJsonOptions);

    /// <summary>A stored tool result, re-serialized without escapes for the gate.</summary>
    private static string EvidenceFromStored(string stored)
    {
        try
        {
            using var doc = JsonDocument.Parse(stored);
            return JsonSerializer.Serialize(doc.RootElement, EvidenceJsonOptions);
        }
        catch (JsonException)
        {
            return stored;
        }
    }

    /// <summary>
    /// History for a «Respaldo obligatorio» bot: user and assistant messages, where each of
    /// the last <see cref="GeminiChatOptions.HistoryToolTurns"/> answers carries the lookups
    /// that backed it (results trimmed for the model). Also returns those results in full,
    /// as evidence for the gate.
    /// </summary>
    private async Task<(List<GeminiChatMessage> History, List<string> Evidence)> LoadHistoryWithEvidenceAsync(
        int conversationId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken);

        // Tool rows are stored in pairs, call then result, between a question and its answer.
        var history = new List<GeminiChatMessage>();
        var exchangesByAnswer = new Dictionary<int, List<(string Name, JsonElement Args, string Result)>>();
        var pending = new List<(string Name, JsonElement Args, string Result)>();
        string? pendingCall = null;

        foreach (var row in rows)
        {
            if (row.Role == MessageRole.Tool)
            {
                if (pendingCall is null)
                {
                    pendingCall = row.Content;
                    continue;
                }

                if (TryParseCall(pendingCall, out var name, out var args))
                    pending.Add((name, args, row.Content));
                pendingCall = null;
                continue;
            }

            if (row.Role == MessageRole.User)
            {
                // Lookups of a turn that never got an answer stay out.
                pending.Clear();
                pendingCall = null;
                history.Add(new GeminiChatMessage(MessageRole.User, row.Content, null));
                continue;
            }

            if (pending.Count > 0)
                exchangesByAnswer[history.Count] = [.. pending];
            history.Add(new GeminiChatMessage(row.Role, row.Content, null));
            pending.Clear();
            pendingCall = null;
        }

        var evidence = new List<string>();
        var turns = Math.Max(0, _chatOptions.Value.HistoryToolTurns);
        var maxChars = Math.Max(200, _chatOptions.Value.HistoryToolResultChars);
        foreach (var index in exchangesByAnswer.Keys.OrderByDescending(k => k).Take(turns))
        {
            var exchanges = exchangesByAnswer[index]
                .Select(e => new HistoryToolExchange(
                    e.Name,
                    e.Args,
                    e.Result.Length <= maxChars ? e.Result : e.Result[..maxChars]))
                .ToList();
            history[index] = history[index] with { ToolExchanges = exchanges };
            evidence.AddRange(exchangesByAnswer[index].Select(e => EvidenceFromStored(e.Result)));
        }

        return (history, evidence);
    }

    private static bool TryParseCall(string content, out string name, out JsonElement args)
    {
        name = string.Empty;
        args = default;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("name", out var n) || n.GetString() is not { Length: > 0 } parsedName)
                return false;
            name = parsedName;
            args = doc.RootElement.TryGetProperty("args", out var a) ? a.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private const string ContinuationNote =
        "[Nota de la plataforma] Tu respuesta anterior se cortó por longitud. "
        + "Continúa exactamente donde quedó, sin repetir nada de lo ya escrito y sin saludar de nuevo.";

    private static string CapNote(IReadOnlyCollection<string> notConsulted) =>
        "[Nota de la plataforma] Se alcanzó el límite de consultas de este turno. "
        + "No pidas más herramientas. Responde ahora solo con la información ya obtenida, "
        + "di con claridad qué parte de la pregunta no pudiste consultar y no inventes datos para completarla."
        + (notConsulted.Count > 0 ? $" Quedó sin consultar: {string.Join(", ", notConsulted.Distinct())}." : string.Empty);

    /// <summary>
    /// Adds a text part to the trailing user content (the function responses) so the
    /// contents keep alternating model/user; a separate user turn would break that.
    /// </summary>
    private static void AppendNoteToLastUserContent(List<GeminiExtraContent> extraContents, string note)
    {
        if (extraContents.Count > 0 && extraContents[^1].Role == "user")
        {
            var last = extraContents[^1];
            extraContents[^1] = last with { Parts = [.. last.Parts, new { text = note }] };
            return;
        }

        extraContents.Add(new GeminiExtraContent("user", [new { text = note }]));
    }

    /// <summary>
    /// A failure worth one more try: server error, the call's own time limit or a
    /// dropped connection. A 400/401/403 would fail the same way again, and a
    /// cancellation from the caller (barge-in) is not a failure. A 429 is not retried
    /// either: it means a quota is spent, and the quota window does not empty in the
    /// seconds a retry could wait (in a load test every one of ten retries failed too,
    /// each adding seconds to the visitor's wait). The turn fails at once, with a clear
    /// message, and the call that would have been rejected is not made.
    /// </summary>
    private static bool IsTransientProviderFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        return ex switch
        {
            ProviderServiceException pse => pse.StatusCode is 500 or 502 or 503 or 504,
            TimeoutException => true,
            OperationCanceledException => true, // HttpClient's own timeout
            HttpRequestException => true,
            IOException => true,
            _ => false,
        };
    }

    private int RetryDelayMs() => Math.Max(0, _chatOptions.Value.RetryDelayMilliseconds);

    private static string DescribeFailure(Exception ex) => ex switch
    {
        ProviderServiceException pse => $"Gemini HTTP {pse.StatusCode?.ToString() ?? "?"} ({pse.Info.ErrorCode})"
            + (pse.RetryAfter is { } wait ? $", asks to wait {wait.TotalSeconds:0.#} s" : string.Empty),
        TimeoutException timeout => $"Gemini timeout ({timeout.Message})",
        OperationCanceledException => "Gemini timeout",
        _ => $"{ex.GetType().Name}",
    };

    private async Task EmitBestEffortAsync(TelemetryEmit emit)
    {
        try
        {
            await _telemetry.EmitAsync(emit, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record telemetry event {EventName}", emit.EventName);
        }
    }

    private sealed record ToolCallOutcome(object Payload, bool KbWithoutResults);

    /// <summary>
    /// Runs one function call and records it (call and result as Tool messages, the
    /// tool's telemetry, a waterfall segment). Never throws for a tool problem: a tool
    /// the bot does not have, or one that fails, comes back as an error result the model
    /// reads — and by the bot's rules it then says it could not consult that.
    /// </summary>
    private async Task<ToolCallOutcome> ExecuteToolCallAsync(
        GeminiFunctionCall call,
        Bot bot,
        Conversation conv,
        Guid? turnId,
        List<object> segments,
        CancellationToken cancellationToken)
    {
        var callContent =
            "{\"name\":"
            + JsonSerializer.Serialize(call.Name)
            + ",\"args\":"
            + call.Args.GetRawText()
            + "}";

        _db.Messages.Add(new Message
        {
            ConversationId = conv.Id,
            Role = MessageRole.Tool,
            Content = callContent,
            Provider = ChatMessageProviders.GeminiTool,
        });
        await _db.SaveChangesAsync(cancellationToken);

        var toolSw = Stopwatch.StartNew();
        var handler = _toolRegistry.Get(call.Name);
        ToolInvocation? invocation = null;
        object payload;

        if (handler is null)
        {
            // Observed: the model asked for "places", a tool this bot does not have.
            _logger.LogWarning(
                "Model requested unknown tool {Tool} for conversation {ConversationId}",
                call.Name,
                conv.Id);
            payload = new
            {
                error = "herramienta_inexistente",
                detalle = $"No existe una herramienta llamada '{call.Name}'. No la vuelvas a pedir: "
                    + "usa solo las herramientas disponibles o di que no puedes consultar ese dato.",
            };
            await EmitBestEffortAsync(new TelemetryEmit(
                "tool_call_unknown",
                EventSource.Backend,
                BotId: conv.BotId,
                ConversationId: conv.Id,
                UserId: conv.UserId,
                TurnId: turnId,
                Stage: "tool",
                Provider: ChatMessageProviders.GeminiTool,
                Summary: $"unknown tool '{call.Name}'"));
        }
        else
        {
            try
            {
                invocation = await handler.ExecuteAsync(
                    call.Args,
                    new ToolContext(bot, conv, turnId),
                    cancellationToken);
                payload = invocation.ResponsePayload;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Tool {Tool} failed for conversation {ConversationId}", call.Name, conv.Id);
                payload = new
                {
                    error = "fallo_de_herramienta",
                    detalle = "La consulta falló. Di que no pudiste consultarlo en este momento; no inventes el resultado.",
                };
                await EmitBestEffortAsync(new TelemetryEmit(
                    "tool_call_failed",
                    EventSource.Backend,
                    BotId: conv.BotId,
                    ConversationId: conv.Id,
                    UserId: conv.UserId,
                    TurnId: turnId,
                    Stage: "tool",
                    Provider: ChatMessageProviders.GeminiTool,
                    Summary: $"{call.Name} threw {ex.GetType().Name}"));
            }
        }

        var toolLatencyMs = (int)toolSw.ElapsedMilliseconds;
        segments.Add(new
        {
            type = "tool",
            name = call.Name,
            ms = toolLatencyMs,
        });

        _db.Messages.Add(new Message
        {
            ConversationId = conv.Id,
            Role = MessageRole.Tool,
            Content = JsonSerializer.Serialize(payload),
            Provider = ChatMessageProviders.GeminiTool,
        });
        await _db.SaveChangesAsync(cancellationToken);

        if (invocation?.Telemetry is { } toolTelemetry)
        {
            var displayPayload = invocation.Display is not null
                ? new
                {
                    name = handler!.Name,
                    items = invocation.Display
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
                TurnId: turnId,
                Stage: toolTelemetry.Stage,
                LatencyMs: toolLatencyMs,
                Provider: ChatMessageProviders.GeminiTool,
                Summary: invocation.Summary,
                Data: JsonSerializer.SerializeToElement(mergedData)),
                cancellationToken);
        }

        return new ToolCallOutcome(
            payload,
            invocation is { NoUsableResults: true } && call.Name == KbSearchToolHandler.ToolName);
    }

    /// <summary>Sum of the tokens Gemini reported across every call of one turn.</summary>
    private sealed class TurnUsageTotals
    {
        public int Calls { get; private set; }
        public int UnreportedCalls { get; private set; }
        public int PromptTokens { get; private set; }
        public int OutputTokens { get; private set; }
        public int CachedTokens { get; private set; }
        public int ThoughtsTokens { get; private set; }
        public int TotalTokens { get; private set; }

        public void Add(GeminiUsage? usage)
        {
            Calls++;
            if (usage is null)
            {
                UnreportedCalls++;
                return;
            }

            PromptTokens += usage.PromptTokens;
            OutputTokens += usage.OutputTokens;
            CachedTokens += usage.CachedTokens;
            ThoughtsTokens += usage.ThoughtsTokens;
            TotalTokens += usage.TotalTokens;
        }

        public object ToData() => new
        {
            calls = Calls,
            unreportedCalls = UnreportedCalls,
            promptTokens = PromptTokens,
            outputTokens = OutputTokens,
            cachedTokens = CachedTokens,
            thoughtsTokens = ThoughtsTokens,
            totalTokens = TotalTokens,
            cachedPercent = PromptTokens > 0
                ? (int)Math.Round(100.0 * CachedTokens / PromptTokens)
                : 0,
        };
    }

    /// <summary>The function-calling mode a call was sent with, as Gemini names it.</summary>
    private static string ToolModeName(ToolChoice toolChoice) => toolChoice switch
    {
        ToolChoice.None => "NONE",
        ToolChoice.Required => "ANY",
        _ => "AUTO",
    };

    /// <summary>
    /// One line for the conversation's events: why Gemini gave no answer, in Google's own terms
    /// (finishReason, blockReason, finishMessage, flagged safety categories), and what the call asked of it.
    /// </summary>
    private static string DescribeNoAnswer(
        GeminiFinish? finish,
        GeminiPromptBlocked? block,
        ToolChoice toolChoice,
        int ignoredToolCalls)
    {
        var parts = new List<string>();
        if (block is not null)
            parts.Add($"Google bloqueó la consulta (blockReason={block.Reason})");
        if (finish is not null)
            parts.Add($"finishReason={finish.Reason}");
        if (block is null && finish is null)
            parts.Add("Google no dio motivo");
        if (!string.IsNullOrEmpty(finish?.Message))
            parts.Add($"mensaje de Google: {TruncateForSummary(finish.Message, 200)}");
        if ((block?.SafetyRatings ?? finish?.SafetyRatings) is { } ratings)
            parts.Add($"categorías de seguridad: {ratings}");
        parts.Add(toolChoice switch
        {
            ToolChoice.Required => "con consulta obligatoria (ANY)",
            ToolChoice.None when ignoredToolCalls > 0 => $"pidió {ignoredToolCalls} herramienta(s) sin permiso (NONE)",
            ToolChoice.None => "sin herramientas (NONE)",
            _ => "con herramientas a su elección (AUTO)",
        });
        return "Gemini no respondió: " + string.Join(" · ", parts);
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
