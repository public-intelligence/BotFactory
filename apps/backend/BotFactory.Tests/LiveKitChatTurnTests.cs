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
    public async Task ChatTurn_ToolLoopCap_StreamsFallbackAfterRepeatedFunctionCalls()
    {
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
