using System.Net;
using System.Text;
using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using BotFactory.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotFactory.Tests;

[Collection("LiveKitTurn")]
public class LiveKitChatTurnTests
{
    private readonly LiveKitWebApplicationFactory _factory;

    public LiveKitChatTurnTests(LiveKitWebApplicationFactory factory) => _factory = factory;

    private static WebApplicationFactory<Program> WithTurnGemini(
        LiveKitWebApplicationFactory baseFactory,
        HttpMessageHandler geminiHandler,
        int maxToolIterations = 2,
        string fallbackMessage = "Lo siento, no pude procesar eso.")
    {
        return baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOptions<GeminiChatOptions>>();
                services.AddSingleton(Options.Create(new GeminiChatOptions
                {
                    MaxToolIterations = maxToolIterations,
                    FallbackMessage = fallbackMessage,
                    RetryDelayMilliseconds = 0,
                }));

                services.RemoveAll<GeminiChatService>();
                services.AddScoped<GeminiChatService>(sp =>
                {
                    var http = new HttpClient(geminiHandler) { Timeout = TimeSpan.FromSeconds(30) };
                    return new GeminiChatService(
                        http,
                        sp.GetRequiredService<IAppSettingService>(),
                        sp.GetRequiredService<IOptions<GeminiChatOptions>>(),
                        sp.GetRequiredService<ILogger<GeminiChatService>>(),
                        sp.GetRequiredService<IToolRegistry>());
                });
            });
        });
    }

    private sealed class SseQueueHandler : HttpMessageHandler
    {
        private readonly Queue<string> _streamBodies = new();

        public void EnqueueStream(string sseBody) => _streamBodies.Enqueue(sseBody);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("streamGenerateContent", StringComparison.Ordinal))
            {
                var body = _streamBodies.Count > 0
                    ? _streamBodies.Dequeue()
                    : "data: {\"candidates\":[]}\n\n";

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
                });
            }

            if (path.Contains("generateContent", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"candidates":[{"content":{"parts":[{"text":"SUM"}]}}]}""",
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>Like <see cref="SseQueueHandler"/> but also records every request body.</summary>
    private sealed class CapturingSseHandler : HttpMessageHandler
    {
        private readonly Queue<string> _streamBodies = new();
        public List<string> RequestBodies { get; } = [];

        public void EnqueueStream(string sseBody) => _streamBodies.Enqueue(sseBody);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            if (request.RequestUri!.AbsolutePath.Contains("streamGenerateContent", StringComparison.Ordinal))
            {
                var body = _streamBodies.Count > 0
                    ? _streamBodies.Dequeue()
                    : "data: {\"candidates\":[]}\n\n";

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static async Task<int> SeedConversationAsync(BotFactoryDbContext db, ITenantContextSetter tenant, Action<Bot>? configureBot = null)
    {
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "LiveKit Turn Bot",
            UrlSlug = $"lk-{Guid.NewGuid():N}",
            PublishedState = "draft",
            MasterPrompt = "You are a test bot.",
        };
        configureBot?.Invoke(bot);
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = "turn-tester",
            Status = ConversationStatus.Active,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        return conv.Id;
    }

    [Fact]
    public async Task ChatTurn_TextOnly_PersistsUserAndAssistant_NoToolRows()
    {
        var handler = new SseQueueHandler();
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Hello"}]}}]}

            """);

        using var app = WithTurnGemini(_factory, handler);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        var client = app.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(new { conversationId = convId, text = "Hi", turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: Hello", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var messages = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == convId)
            .OrderBy(m => m.Id)
            .ToListAsync();

        Assert.True(messages.Count >= 2);
        Assert.Equal(MessageRole.User, messages[^2].Role);
        Assert.Equal("Hi", messages[^2].Content);
        Assert.Equal(MessageRole.Assistant, messages[^1].Role);
        Assert.Equal("Hello", messages[^1].Content);
        Assert.Equal("gemini", messages[^1].Provider);
        Assert.DoesNotContain(messages, m => m.Role == MessageRole.Tool);
    }

    [Fact]
    public async Task ChatTurn_DanglingUserFromCancelledTurn_DroppedFromHistory()
    {
        var handler = new CapturingSseHandler();
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Respuesta"}]}}]}

            """);

        using var app = WithTurnGemini(_factory, handler);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        // Simulate a prior turn cancelled mid-flight (barge-in / superseded): the
        // user message was persisted but no assistant reply followed it.
        db.Messages.Add(new Message
        {
            ConversationId = convId,
            Role = MessageRole.User,
            Content = "PREGUNTA_ABANDONADA_ZZ",
        });
        await db.SaveChangesAsync();

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(
            new { conversationId = convId, text = "nueva pregunta actual", turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();

        // No tool call -> exactly one Gemini pass. The abandoned, unanswered
        // question must NOT be sent (it would sit right before the current
        // question as a second consecutive user turn); the current question must.
        var geminiBody = Assert.Single(handler.RequestBodies);
        Assert.DoesNotContain("PREGUNTA_ABANDONADA_ZZ", geminiBody, StringComparison.Ordinal);
        Assert.Contains("nueva pregunta actual", geminiBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatTurn_FunctionCallThenText_PersistsToolRows()
    {
        var handler = new SseQueueHandler();
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"kb_search","args":{"query":"q"}}}]}}]}

            """);
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"From KB"}]}}]}

            """);

        using var app = WithTurnGemini(_factory, handler);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        var client = app.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(new { conversationId = convId, text = "Search please", turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("From KB", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var messages = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == convId)
            .OrderBy(m => m.Id)
            .ToListAsync();

        var toolRows = messages.Where(m => m.Role == MessageRole.Tool).ToList();
        Assert.Equal(2, toolRows.Count);
        Assert.All(toolRows, m => Assert.Equal(ChatMessageProviders.GeminiTool, m.Provider));
        Assert.Equal("From KB", messages[^1].Content);
    }

    [Fact]
    public async Task ChatTurn_GroundedOnly_WhenNoKbHits_StreamsGroundedMessage()
    {
        var handler = new SseQueueHandler();
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"kb_search","args":{"query":"x"}}}]}}]}

            """);

        using var app = WithTurnGemini(_factory, handler);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant, b =>
        {
            b.GroundedOnlyMessage = "USE_GROUNDED_ONLY";
            b.SimilarityThreshold = 0.01;
        });

        var client = app.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(new { conversationId = convId, text = "Need docs", turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("USE_GROUNDED_ONLY", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var assistant = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == convId && m.Role == MessageRole.Assistant)
            .OrderByDescending(m => m.Id)
            .FirstAsync();

        Assert.Equal("USE_GROUNDED_ONLY", assistant.Content);
        Assert.Equal(ChatMessageProviders.GeminiGroundedOnly, assistant.Provider);
    }

    [Fact]
    public async Task ChatTurn_ModelInsistsOnToolsAfterTheCap_StreamsFallback()
    {
        // Cap of 1: the first call runs, the second exceeds the budget, so the last
        // pass forbids tools (NONE). A model that still answers with a function call
        // gets nothing more: the turn ends, and with no text the fallback is spoken.
        var handler = new SseQueueHandler();
        var fcLine =
            """
            data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"kb_search","args":{"query":"x"}}}]}}]}

            """;
        handler.EnqueueStream(fcLine);
        handler.EnqueueStream(fcLine);
        handler.EnqueueStream(fcLine);

        using var app = WithTurnGemini(_factory, handler, maxToolIterations: 1, fallbackMessage: "FALLBACK_CAP");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        var client = app.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(new { conversationId = convId, text = "Loop", turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("FALLBACK_CAP", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var assistant = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == convId && m.Role == MessageRole.Assistant)
            .OrderByDescending(m => m.Id)
            .FirstAsync();

        Assert.Equal("FALLBACK_CAP", assistant.Content);
        Assert.Equal("gemini-fallback", assistant.Provider);
    }

    [Fact]
    public async Task ChatTurn_EmptyModelResponse_StreamsFallback()
    {
        var handler = new SseQueueHandler();
        // Stream with no text parts at all — the model "answers" nothing.
        handler.EnqueueStream("data: {\"candidates\":[]}\n\n");

        using var app = WithTurnGemini(_factory, handler, fallbackMessage: "FALLBACK_EMPTY");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        var client = app.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(new { conversationId = convId, text = "Hola", turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("FALLBACK_EMPTY", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var assistant = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == convId && m.Role == MessageRole.Assistant)
            .OrderByDescending(m => m.Id)
            .FirstAsync();

        Assert.Equal("FALLBACK_EMPTY", assistant.Content);
        Assert.Equal("gemini-fallback", assistant.Provider);
    }

    [Fact]
    public async Task ChatTurn_WhitespaceOnlyModelResponse_StreamsFallback()
    {
        var handler = new SseQueueHandler();
        // The model streams only whitespace — audibly identical to silence.
        handler.EnqueueStream(
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"  \"}]}}]}\n\n"
            + "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\" \"}]}}]}\n\n");

        using var app = WithTurnGemini(_factory, handler, fallbackMessage: "FALLBACK_WS");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(new { conversationId = convId, text = "Hola", turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("FALLBACK_WS", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var assistant = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == convId && m.Role == MessageRole.Assistant)
            .OrderByDescending(m => m.Id)
            .FirstAsync();

        Assert.Equal("FALLBACK_WS", assistant.Content);
        Assert.Equal("gemini-fallback", assistant.Provider);
    }

    [Fact]
    public async Task ChatTurn_CandidatesWithoutParts_StreamsFallback()
    {
        var handler = new SseQueueHandler();
        // 200 with a candidate that carries no parts (e.g. safety-blocked).
        handler.EnqueueStream("data: {\"candidates\":[{\"content\":{\"parts\":[]}}]}\n\n");

        using var app = WithTurnGemini(_factory, handler, fallbackMessage: "FALLBACK_NOPARTS");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(new { conversationId = convId, text = "Hola", turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("FALLBACK_NOPARTS", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var assistant = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == convId && m.Role == MessageRole.Assistant)
            .OrderByDescending(m => m.Id)
            .FirstAsync();

        Assert.Equal("FALLBACK_NOPARTS", assistant.Content);
        Assert.Equal("gemini-fallback", assistant.Provider);
    }

    private sealed class GeminiHttpErrorHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public GeminiHttpErrorHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("streamGenerateContent", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task ChatTurn_Gemini429_StreamsProviderErrorFallbackAndTelemetry()
    {
        var handler = new GeminiHttpErrorHandler(
            HttpStatusCode.TooManyRequests,
            """{"error":{"status":"RESOURCE_EXHAUSTED","message":"Quota exceeded"}}""");

        using var app = WithTurnGemini(_factory, handler, fallbackMessage: "FALLBACK_QUOTA");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        var client = app.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(new { conversationId = convId, text = "Hola", turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: provider_error", sse, StringComparison.Ordinal);
        Assert.Contains("gemini_quota_exceeded", sse, StringComparison.Ordinal);
        Assert.Contains("FALLBACK_QUOTA", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var llmError = await vdb.TelemetryEvents
            .IgnoreQueryFilters()
            .Where(e => e.ConversationId == convId && e.EventName == "llm_error")
            .OrderByDescending(e => e.Id)
            .FirstAsync();

        Assert.Contains("Gemini", llmError.Summary!, StringComparison.Ordinal);

        // The call failed before Gemini counted anything: llm_error covers it, and an
        // llm_usage row with no numbers would only be noise.
        Assert.Empty(await TelemetryOfAsync(app, convId, "llm_usage"));
    }

    /// <summary>
    /// Answers each Gemini call from a script of (status, body) and records every
    /// request body, so a test can drive errors, retries and multi-pass turns.
    /// </summary>
    private sealed class ScriptedGeminiHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _script = new();
        public List<string> RequestBodies { get; } = [];

        public void Enqueue(string sseBody) => _script.Enqueue((HttpStatusCode.OK, sseBody));

        public void EnqueueError(HttpStatusCode status, string body = "{}") => _script.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.Contains("streamGenerateContent", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.Content is not null)
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            var (status, body) = _script.Count > 0
                ? _script.Dequeue()
                : (HttpStatusCode.OK, "data: {\"candidates\":[]}\n\n");
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    status == HttpStatusCode.OK ? "text/event-stream" : "application/json"),
            };
        }
    }

    private static string TextStream(string text, string finishReason = "STOP") =>
        "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + JsonSerializer.Serialize(text)
        + "}]},\"finishReason\":\"" + finishReason + "\"}]}\n\n";

    private static string CallsStream(params string[] names) =>
        "data: {\"candidates\":[{\"content\":{\"parts\":["
        + string.Join(",", names.Select((n, i) =>
            "{\"functionCall\":{\"name\":\"" + n + "\",\"args\":{\"query\":\"q" + i + "\"}}}"))
        + "]},\"finishReason\":\"STOP\"}]}\n\n";

    private static string ToolChoiceOf(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        return doc.RootElement.GetProperty("toolConfig").GetProperty("functionCallingConfig").GetProperty("mode").GetString()!;
    }

    private static async Task<List<Message>> MessagesOfAsync(WebApplicationFactory<Program> app, int convId)
    {
        using var scope = app.Services.CreateScope();
        var vdb = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        return await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == convId)
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    private async Task<(WebApplicationFactory<Program> App, int ConvId)> AppWithConversationAsync(
        ScriptedGeminiHandler handler,
        int maxToolIterations = 6,
        string fallbackMessage = "FALLBACK_P2")
    {
        var app = WithTurnGemini(_factory, handler, maxToolIterations: maxToolIterations, fallbackMessage: fallbackMessage);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        return (app, await SeedConversationAsync(db, tenant));
    }

    [Fact]
    public async Task ChatTurn_SeveralCallsInOneAnswer_AllRunInOnePass()
    {
        // A multi-question message: the model asks for three lookups at once. All three
        // must run before the next Gemini call (before, only the first one ran and each
        // question cost a whole extra pass).
        var handler = new ScriptedGeminiHandler();
        handler.Enqueue(CallsStream("kb_search", "kb_search", "kb_search"));
        handler.Enqueue(TextStream("Tres respuestas juntas."));

        var (app, convId) = await AppWithConversationAsync(handler);
        using var _ = app;
        var sse = await PostTurnAsync(app, convId, "Tres preguntas");

        Assert.Contains("Tres respuestas juntas.", sse, StringComparison.Ordinal);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Equal(3, CountOccurrences(handler.RequestBodies[1], "\"functionResponse\""));

        var messages = await MessagesOfAsync(app, convId);
        Assert.Equal(6, messages.Count(m => m.Role == MessageRole.Tool));
        Assert.Equal("gemini", messages[^1].Provider);

        var completed = Assert.Single(await TelemetryOfAsync(app, convId, "backend_completed"));
        using var turn = JsonDocument.Parse(completed.Data!);
        Assert.Equal(3, turn.RootElement.GetProperty("toolCount").GetInt32());
        Assert.False(turn.RootElement.GetProperty("toolCapReached").GetBoolean());
    }

    [Fact]
    public async Task ChatTurn_ToolCapReached_AnswersWithWhatWasGathered_InsteadOfFallback()
    {
        var handler = new ScriptedGeminiHandler();
        handler.Enqueue(CallsStream("kb_search", "kb_search", "kb_search"));
        handler.Enqueue(TextStream("Le respondo con lo que pude consultar."));

        var (app, convId) = await AppWithConversationAsync(handler, maxToolIterations: 2, fallbackMessage: "FALLBACK_CAP2");
        using var _ = app;
        var sse = await PostTurnAsync(app, convId, "Tres preguntas");

        Assert.Contains("Le respondo con lo que pude consultar.", sse, StringComparison.Ordinal);
        Assert.DoesNotContain("FALLBACK_CAP2", sse, StringComparison.Ordinal);

        // Two of the three ran; the last pass forbade tools and told the model why.
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Equal(2, CountOccurrences(handler.RequestBodies[1], "\"functionResponse\""));
        Assert.Equal("NONE", ToolChoiceOf(handler.RequestBodies[1]));
        Assert.Contains("límite de consultas", Unescaped(handler.RequestBodies[1]), StringComparison.Ordinal);
        Assert.Contains("Quedó sin consultar: kb_search", Unescaped(handler.RequestBodies[1]), StringComparison.Ordinal);

        var messages = await MessagesOfAsync(app, convId);
        Assert.Equal(4, messages.Count(m => m.Role == MessageRole.Tool));
        Assert.Equal("gemini", messages[^1].Provider);
        Assert.Single(await TelemetryOfAsync(app, convId, "tool_cap_reached"));
    }

    [Fact]
    public async Task ChatTurn_UnknownTool_IsAnsweredAsAFunctionError_NotAFailedTurn()
    {
        // Observed in production: the model asked for "places", which the bot lacks,
        // and the exception ended the turn in the fallback message.
        var handler = new ScriptedGeminiHandler();
        handler.Enqueue(CallsStream("places"));
        handler.Enqueue(TextStream("No puedo consultar eso en este momento."));

        var (app, convId) = await AppWithConversationAsync(handler, fallbackMessage: "FALLBACK_UNKNOWN");
        using var _ = app;
        var sse = await PostTurnAsync(app, convId, "¿Dónde queda?");

        Assert.Contains("No puedo consultar eso en este momento.", sse, StringComparison.Ordinal);
        Assert.DoesNotContain("FALLBACK_UNKNOWN", sse, StringComparison.Ordinal);
        Assert.Contains("herramienta_inexistente", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Single(await TelemetryOfAsync(app, convId, "tool_call_unknown"));
        Assert.Equal("gemini", (await MessagesOfAsync(app, convId))[^1].Provider);
    }

    [Fact]
    public async Task ChatTurn_Gemini503ThenOk_RetriesOnceAndAnswers()
    {
        var handler = new ScriptedGeminiHandler();
        handler.EnqueueError(HttpStatusCode.ServiceUnavailable, """{"error":{"status":"UNAVAILABLE"}}""");
        handler.Enqueue(TextStream("Respuesta tras reintentar."));

        var (app, convId) = await AppWithConversationAsync(handler, fallbackMessage: "FALLBACK_UNAVAILABLE");
        using var _ = app;
        var sse = await PostTurnAsync(app, convId, "Hola");

        Assert.Contains("Respuesta tras reintentar.", sse, StringComparison.Ordinal);
        Assert.DoesNotContain("FALLBACK_UNAVAILABLE", sse, StringComparison.Ordinal);
        Assert.DoesNotContain("event: provider_error", sse, StringComparison.Ordinal);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Single(await TelemetryOfAsync(app, convId, "llm_retry"));
        Assert.Equal("gemini", (await MessagesOfAsync(app, convId))[^1].Provider);
    }

    [Fact]
    public async Task ChatTurn_Gemini503Twice_FallsBackAfterOneRetryOnly()
    {
        var handler = new ScriptedGeminiHandler();
        handler.EnqueueError(HttpStatusCode.ServiceUnavailable);
        handler.EnqueueError(HttpStatusCode.ServiceUnavailable);
        handler.Enqueue(TextStream("No debe usarse."));

        var (app, convId) = await AppWithConversationAsync(handler, fallbackMessage: "FALLBACK_TWICE");
        using var _ = app;
        var sse = await PostTurnAsync(app, convId, "Hola");

        Assert.Contains("FALLBACK_TWICE", sse, StringComparison.Ordinal);
        Assert.DoesNotContain("No debe usarse.", sse, StringComparison.Ordinal);
        Assert.Equal(2, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task ChatTurn_Gemini400_IsNotRetried()
    {
        // A malformed request fails the same way twice: retrying only adds latency.
        var handler = new ScriptedGeminiHandler();
        handler.EnqueueError(HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT"}}""");
        handler.Enqueue(TextStream("No debe usarse."));

        var (app, convId) = await AppWithConversationAsync(handler, fallbackMessage: "FALLBACK_BAD_REQUEST");
        using var _ = app;
        var sse = await PostTurnAsync(app, convId, "Hola");

        Assert.Contains("FALLBACK_BAD_REQUEST", sse, StringComparison.Ordinal);
        Assert.Single(handler.RequestBodies);
        Assert.Empty(await TelemetryOfAsync(app, convId, "llm_retry"));
    }

    [Fact]
    public async Task ChatTurn_AnswerCutAtTheOutputLimit_IsContinuedOnce_SoNoDatumIsLeftHalf()
    {
        // Observed on staging: a six-question answer ended in "info@pgr.gob" because
        // Gemini hit the output limit. What was streamed stays; one more pass, with
        // tools forbidden, finishes it.
        var handler = new ScriptedGeminiHandler();
        handler.Enqueue(TextStream("El correo es info@pgr.gob", finishReason: "MAX_TOKENS"));
        handler.Enqueue(TextStream(".do y el teléfono 809-533-3522."));

        var (app, convId) = await AppWithConversationAsync(handler);
        using var _ = app;
        var sse = await PostTurnAsync(app, convId, "Datos de la Procuraduría");

        // The SSE is the spoken text (the voice sanitizer drops numbers and addresses);
        // the original, complete answer is what the conversation stores.
        Assert.Contains("El correo es", sse, StringComparison.Ordinal);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Equal("NONE", ToolChoiceOf(handler.RequestBodies[1]));
        Assert.Contains("se cortó por longitud", Unescaped(handler.RequestBodies[1]), StringComparison.Ordinal);
        Assert.Contains("El correo es info@pgr.gob", handler.RequestBodies[1], StringComparison.Ordinal);

        var messages = await MessagesOfAsync(app, convId);
        Assert.Equal("El correo es info@pgr.gob.do y el teléfono 809-533-3522.", messages[^1].Content);
        Assert.Single(await TelemetryOfAsync(app, convId, "llm_truncated"));

        var completed = Assert.Single(await TelemetryOfAsync(app, convId, "backend_completed"));
        using var turn = JsonDocument.Parse(completed.Data!);
        Assert.True(turn.RootElement.GetProperty("continued").GetBoolean());
    }

    /// <summary>The request JSON with non-ASCII characters unescaped ("l\u00EDmite" → "límite").</summary>
    private static string Unescaped(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    [Fact]
    public async Task ChatTurn_LineBreaks_AreKeptForTheChat_AndFlattenedOnlyForVoice()
    {
        // A list is only a list if its line breaks survive. The stored message (what the
        // chat shows) keeps them; the voice SSE gets one line, as before, so no frame breaks.
        var handler = new ScriptedGeminiHandler();
        handler.Enqueue(TextStream("Los requisitos son:\n- Cita impresa.\n- Cédula vigente.\n\nEl costo lo publica Pasaportes."));

        var (app, convId) = await AppWithConversationAsync(handler);
        using var _ = app;
        var sse = await PostTurnAsync(app, convId, "Requisitos del pasaporte");

        var stored = (await MessagesOfAsync(app, convId))[^1];
        Assert.Equal(MessageRole.Assistant, stored.Role);
        Assert.Equal("Los requisitos son:\n- Cita impresa.\n- Cédula vigente.\n\nEl costo lo publica Pasaportes.", stored.Content);

        // Every non-empty SSE line is a field or a comment: no stray text line from a '\n'.
        var lines = sse.Split('\n').Where(l => l.Length > 0).ToList();
        Assert.All(lines, l => Assert.True(
            l.StartsWith("data:", StringComparison.Ordinal)
            || l.StartsWith("event:", StringComparison.Ordinal)
            || l.StartsWith(':'),
            $"unexpected SSE line: {l}"));
        Assert.Contains("Cita impresa.", sse, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static async Task<string> PostTurnAsync(WebApplicationFactory<Program> app, int convId, string text)
    {
        var client = app.CreateClient();
        // /livekit/* is gated by the agent shared secret (appsettings.Development.json).
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var payload = JsonSerializer.Serialize(new { conversationId = convId, text, turnId = Guid.NewGuid() });
        var response = await client.PostAsync(
            "/livekit/chat/turn",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<List<TelemetryEvent>> TelemetryOfAsync(
        WebApplicationFactory<Program> app, int convId, string eventName)
    {
        using var scope = app.Services.CreateScope();
        var vdb = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        return await vdb.TelemetryEvents
            .IgnoreQueryFilters()
            .Where(e => e.ConversationId == convId && e.EventName == eventName)
            .OrderBy(e => e.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task ChatTurn_ReportsGeminiUsage_LastChunkWins_AndFeedsTurnSummary()
    {
        // Gemini repeats usageMetadata on every chunk with running counts; only the
        // last one describes the call.
        var handler = new SseQueueHandler();
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Hola"}]}}],"usageMetadata":{"promptTokenCount":1200,"candidatesTokenCount":2,"totalTokenCount":1202,"cachedContentTokenCount":1000}}

            data: {"candidates":[{"content":{"parts":[{"text":" mundo"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":1200,"candidatesTokenCount":20,"totalTokenCount":1220,"cachedContentTokenCount":1000}}

            """);

        using var app = WithTurnGemini(_factory, handler);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        await PostTurnAsync(app, convId, "Hola");

        var usage = Assert.Single(await TelemetryOfAsync(app, convId, "llm_usage"));
        using (var d = JsonDocument.Parse(usage.Data!))
        {
            var r = d.RootElement;
            Assert.True(r.GetProperty("reported").GetBoolean());
            Assert.True(r.GetProperty("complete").GetBoolean());
            Assert.Equal(1, r.GetProperty("call").GetInt32());
            Assert.Equal(1200, r.GetProperty("promptTokens").GetInt32());
            Assert.Equal(1000, r.GetProperty("cachedTokens").GetInt32());
            Assert.Equal(20, r.GetProperty("outputTokens").GetInt32());
            Assert.Equal(0, r.GetProperty("thoughtsTokens").GetInt32());
            Assert.Equal(1220, r.GetProperty("totalTokens").GetInt32());
            Assert.Equal(83, r.GetProperty("cachedPercent").GetInt32());
        }
        Assert.Equal("in=1200 cached=1000 (83%) out=20 thoughts=0 total=1220", usage.Summary);

        var completed = Assert.Single(await TelemetryOfAsync(app, convId, "backend_completed"));
        using var turn = JsonDocument.Parse(completed.Data!);
        var tokens = turn.RootElement.GetProperty("tokens");
        Assert.Equal(1, tokens.GetProperty("calls").GetInt32());
        Assert.Equal(0, tokens.GetProperty("unreportedCalls").GetInt32());
        Assert.Equal(1200, tokens.GetProperty("promptTokens").GetInt32());
        Assert.Equal(1000, tokens.GetProperty("cachedTokens").GetInt32());
        Assert.Equal(20, tokens.GetProperty("outputTokens").GetInt32());

        var gemini = turn.RootElement.GetProperty("segments")[0];
        Assert.Equal("gemini", gemini.GetProperty("type").GetString());
        Assert.Equal(1200, gemini.GetProperty("tokens").GetProperty("promptTokens").GetInt32());
        Assert.Contains("in=1200 cached=1000 out=20", completed.Summary!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatTurn_FunctionCallThenText_RecordsUsageOfEveryGeminiCall_AndSumsTheTurn()
    {
        // The turn stops reading a stream as soon as it sees a function call, so the
        // usage of that call must already have been delivered by then.
        var handler = new SseQueueHandler();
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"kb_search","args":{"query":"q"}}}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":3000,"candidatesTokenCount":15,"totalTokenCount":3015}}

            """);
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"From KB"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":3200,"candidatesTokenCount":30,"totalTokenCount":3230,"cachedContentTokenCount":3000,"thoughtsTokenCount":5}}

            """);

        using var app = WithTurnGemini(_factory, handler);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        await PostTurnAsync(app, convId, "Search please");

        var usage = await TelemetryOfAsync(app, convId, "llm_usage");
        Assert.Equal(2, usage.Count);
        using (var first = JsonDocument.Parse(usage[0].Data!))
        {
            Assert.Equal(1, first.RootElement.GetProperty("call").GetInt32());
            Assert.Equal(3000, first.RootElement.GetProperty("promptTokens").GetInt32());
            Assert.Equal(0, first.RootElement.GetProperty("cachedTokens").GetInt32());
        }
        using (var second = JsonDocument.Parse(usage[1].Data!))
        {
            Assert.Equal(2, second.RootElement.GetProperty("call").GetInt32());
            Assert.Equal(3200, second.RootElement.GetProperty("promptTokens").GetInt32());
            Assert.Equal(3000, second.RootElement.GetProperty("cachedTokens").GetInt32());
            Assert.Equal(5, second.RootElement.GetProperty("thoughtsTokens").GetInt32());
        }

        var completed = Assert.Single(await TelemetryOfAsync(app, convId, "backend_completed"));
        using var turn = JsonDocument.Parse(completed.Data!);
        var tokens = turn.RootElement.GetProperty("tokens");
        Assert.Equal(2, tokens.GetProperty("calls").GetInt32());
        Assert.Equal(6200, tokens.GetProperty("promptTokens").GetInt32());
        Assert.Equal(3000, tokens.GetProperty("cachedTokens").GetInt32());
        Assert.Equal(45, tokens.GetProperty("outputTokens").GetInt32());
        Assert.Equal(5, tokens.GetProperty("thoughtsTokens").GetInt32());
        Assert.Equal(6245, tokens.GetProperty("totalTokens").GetInt32());
        Assert.Equal(48, tokens.GetProperty("cachedPercent").GetInt32());

        // Tokens ride on the Gemini segments only, not on the tool segment.
        var segments = turn.RootElement.GetProperty("segments").EnumerateArray().ToList();
        Assert.Equal(3, segments.Count);
        Assert.Equal(3000, segments[0].GetProperty("tokens").GetProperty("promptTokens").GetInt32());
        Assert.Equal("tool", segments[1].GetProperty("type").GetString());
        Assert.Equal(3200, segments[2].GetProperty("tokens").GetProperty("promptTokens").GetInt32());
    }

    [Fact]
    public async Task ChatTurn_GeminiOmitsUsageMetadata_IsRecordedAsUnreported_AndTurnStillAnswers()
    {
        var handler = new SseQueueHandler();
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Hello"}]}}]}

            """);

        using var app = WithTurnGemini(_factory, handler);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        var convId = await SeedConversationAsync(db, tenant);

        var sse = await PostTurnAsync(app, convId, "Hi");
        Assert.Contains("data: Hello", sse, StringComparison.Ordinal);

        var usage = Assert.Single(await TelemetryOfAsync(app, convId, "llm_usage"));
        using (var d = JsonDocument.Parse(usage.Data!))
        {
            Assert.False(d.RootElement.GetProperty("reported").GetBoolean());
            Assert.Equal(JsonValueKind.Null, d.RootElement.GetProperty("promptTokens").ValueKind);
        }

        var completed = Assert.Single(await TelemetryOfAsync(app, convId, "backend_completed"));
        using var turn = JsonDocument.Parse(completed.Data!);
        Assert.Equal("ok", turn.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(1, turn.RootElement.GetProperty("tokens").GetProperty("unreportedCalls").GetInt32());
        Assert.Equal(JsonValueKind.Null, turn.RootElement.GetProperty("segments")[0].GetProperty("tokens").ValueKind);
    }
}
