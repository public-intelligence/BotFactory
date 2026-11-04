using System.Net;
using System.Text;
using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using BotFactory.Infrastructure.Tools.Connectors;
using BotFactory.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotFactory.Tests;

/// <summary>
/// End-to-end configured-tool execution through the real /livekit/chat/turn
/// pipeline: dynamic per-bot exposure, gateway validation/policy, REST
/// connector against a stubbed external service, execution records, telemetry.
/// </summary>
[Collection("LiveKitTurn")]
public class ExternalToolsChatTurnTests
{
    private readonly LiveKitWebApplicationFactory _factory;

    public ExternalToolsChatTurnTests(LiveKitWebApplicationFactory factory) => _factory = factory;

    // ── Test doubles ───────────────────────────────────────────────────────

    /// <summary>Gemini stub that records every request body and replays queued SSE streams.</summary>
    private sealed class CapturingGeminiHandler : HttpMessageHandler
    {
        private readonly Queue<string> _streamBodies = new();
        public List<string> RequestBodies { get; } = [];

        public void EnqueueStream(string sseBody) => _streamBodies.Enqueue(sseBody);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
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

    /// <summary>The external government-service stub reached through the "tool-egress" client.</summary>
    private sealed class ExternalServiceHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Responder { get; set; } =
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"estado":"aprobado","fecha":"2026-07-01"}""", Encoding.UTF8, "application/json"),
            });

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await Responder(request);
        }
    }

    private static WebApplicationFactory<Program> BuildApp(
        LiveKitWebApplicationFactory baseFactory,
        CapturingGeminiHandler geminiHandler,
        ExternalServiceHandler externalHandler)
    {
        return baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOptions<GeminiChatOptions>>();
                services.AddSingleton(Options.Create(new GeminiChatOptions
                {
                    MaxToolIterations = 2,
                    FallbackMessage = "FALLBACK_EXT",
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

                // Route the tool-egress named client at the external stub.
                services.AddHttpClient(RestToolConnector.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => externalHandler);
            });
        });
    }

    private sealed record SeededTool(
        int BotId, int ConversationId, int ToolDefinitionId, int ConnectionId, string ToolName);

    private static async Task<SeededTool> SeedAsync(
        IServiceProvider scopedServices,
        bool associateTool = true,
        bool publicConversation = false,
        Action<ToolDefinition>? configureDefinition = null)
    {
        var db = scopedServices.GetRequiredService<BotFactoryDbContext>();
        var tenant = scopedServices.GetRequiredService<ITenantContextSetter>();
        var encryption = scopedServices.GetRequiredService<IEncryptionService>();

        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "External Tools Bot",
            UrlSlug = $"ext-{Guid.NewGuid():N}",
            PublishedState = "draft",
            MasterPrompt = "You are a test bot.",
        };
        db.Bots.Add(bot);

        var connection = new ToolConnection
        {
            Name = $"registro-{Guid.NewGuid():N}",
            ConnectorType = ToolConnectorType.Rest,
            BaseUrl = "https://ext-registry.test",
            AuthType = ToolAuthType.ApiKeyHeader,
            AuthConfigJson = """{"headerName":"X-Api-Key"}""",
            EncryptedSecretsJson = encryption.Encrypt("""{"apiKey":"sekret123"}"""),
            SecretPrefix = "sekre",
            TimeoutMs = 2000,
        };
        db.ToolConnections.Add(connection);
        await db.SaveChangesAsync();

        // Unique per test: the whole suite shares one SQL container and the
        // (Name, OrganizationId) index is unique.
        var toolName = $"consultar_estado_{Guid.NewGuid():N}";

        var definition = new ToolDefinition
        {
            ToolConnectionId = connection.Id,
            Name = toolName,
            DisplayName = "Consultar estado de expediente",
            AiDescription = "Consulta el estado actual de un expediente.",
            HttpMethod = "GET",
            PathTemplate = "/estado/{numero}",
            ParametersSchemaJson =
                """{"type":"object","properties":{"numero":{"type":"string"}},"required":["numero"]}""",
            ErrorMappingJson = """{"404":"No se encontró el expediente.","default":"Error del registro."}""",
            Status = ToolDefinitionStatus.Published,
        };
        configureDefinition?.Invoke(definition);
        db.ToolDefinitions.Add(definition);
        await db.SaveChangesAsync();

        if (associateTool)
        {
            db.BotTools.Add(new BotTool
            {
                BotId = bot.Id,
                ToolDefinitionId = definition.Id,
                Enabled = true,
            });
        }

        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = "ext-tester",
            Status = ConversationStatus.Active,
            AccessMode = publicConversation
                ? ConversationAccessModes.Public
                : ConversationAccessModes.Authenticated,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        return new SeededTool(bot.Id, conv.Id, definition.Id, connection.Id, toolName);
    }

    private static HttpClient AgentClient(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        return client;
    }

    private static StringContent TurnPayload(int conversationId, string text) => new(
        JsonSerializer.Serialize(new { conversationId, text, turnId = Guid.NewGuid() }),
        Encoding.UTF8,
        "application/json");

    private static string ToolCallStream(string toolName) =>
        """
        data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"__TOOL__","args":{"numero":"EXP-9"}}}]}}]}

        """.Replace("__TOOL__", toolName, StringComparison.Ordinal);

    private const string FinalAnswerStream =
        """
        data: {"candidates":[{"content":{"parts":[{"text":"Su expediente EXP-9 está aprobado."}]}}]}

        """;

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfiguredTool_ExecutesExternally_RecordsEverything()
    {
        var gemini = new CapturingGeminiHandler();
        var external = new ExternalServiceHandler();

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        var seeded = await SeedAsync(scope.ServiceProvider);
        gemini.EnqueueStream(ToolCallStream(seeded.ToolName));
        gemini.EnqueueStream(FinalAnswerStream);

        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, "¿Cómo va mi expediente EXP-9?"));

        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("está aprobado", sse, StringComparison.Ordinal);

        // The external service saw the templated request with credentials.
        var externalRequest = Assert.Single(external.Requests);
        Assert.Equal("https://ext-registry.test/estado/EXP-9", externalRequest.RequestUri!.AbsoluteUri);
        Assert.Equal("sekret123", externalRequest.Headers.GetValues("X-Api-Key").Single());

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();

        // Governance record.
        var execution = await vdb.ToolExecutions
            .IgnoreQueryFilters()
            .SingleAsync(e => e.ConversationId == seeded.ConversationId);
        Assert.Equal(ToolExecutionOutcome.Success, execution.Outcome);
        Assert.Equal(seeded.ToolName, execution.ToolName);
        Assert.Equal(200, execution.HttpStatus);
        Assert.False(execution.IsTest);
        Assert.Contains("EXP-9", execution.ArgumentsJson, StringComparison.Ordinal);
        Assert.Contains("aprobado", execution.ResultSnippetJson!, StringComparison.Ordinal);
        Assert.Contains("\"parametersSchemaJson\"", execution.DefinitionSnapshotJson, StringComparison.OrdinalIgnoreCase);

        // Chat context rows (call + response) as for kb_search.
        var toolRows = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == seeded.ConversationId && m.Role == MessageRole.Tool)
            .ToListAsync();
        Assert.Equal(2, toolRows.Count);

        // Telemetry.
        var telemetry = await vdb.TelemetryEvents
            .IgnoreQueryFilters()
            .SingleAsync(e => e.ConversationId == seeded.ConversationId && e.EventName == "tool_call_completed");
        Assert.Equal("tool", telemetry.Stage);
    }

    [Fact]
    public async Task UnassociatedBot_DoesNotSeeConfiguredTool_ButKeepsKbSearch()
    {
        var gemini = new CapturingGeminiHandler();
        gemini.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Hola"}]}}]}

            """);
        var external = new ExternalServiceHandler();

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        var seeded = await SeedAsync(scope.ServiceProvider, associateTool: false);

        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, "Hola"));
        response.EnsureSuccessStatusCode();

        var geminiBody = Assert.Single(gemini.RequestBodies);
        Assert.Contains("kb_search", geminiBody, StringComparison.Ordinal);
        Assert.DoesNotContain(seeded.ToolName, geminiBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssociatedBot_ExposesBothKbSearchAndConfiguredTool()
    {
        var gemini = new CapturingGeminiHandler();
        gemini.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Hola"}]}}]}

            """);
        var external = new ExternalServiceHandler();

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        var seeded = await SeedAsync(scope.ServiceProvider);

        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, "Hola"));
        response.EnsureSuccessStatusCode();

        var geminiBody = Assert.Single(gemini.RequestBodies);
        Assert.Contains("kb_search", geminiBody, StringComparison.Ordinal);
        Assert.Contains(seeded.ToolName, geminiBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidArguments_RecordValidationFailure_ModelGetsStructuredError()
    {
        var gemini = new CapturingGeminiHandler();
        var external = new ExternalServiceHandler();

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        var seeded = await SeedAsync(scope.ServiceProvider);
        gemini.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"__TOOL__","args":{}}}]}}]}

            """.Replace("__TOOL__", seeded.ToolName, StringComparison.Ordinal));
        gemini.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"¿Me indica el número de expediente?"}]}}]}

            """);

        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, "Estado de mi expediente"));
        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("número de expediente", sse, StringComparison.Ordinal);

        // No external call happened; the failure is recorded.
        Assert.Empty(external.Requests);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var execution = await vdb.ToolExecutions
            .IgnoreQueryFilters()
            .SingleAsync(e => e.ConversationId == seeded.ConversationId);
        Assert.Equal(ToolExecutionOutcome.ValidationFailed, execution.Outcome);
    }

    [Fact]
    public async Task PublicConversation_ToolRequiringAuth_IsPolicyBlocked()
    {
        var gemini = new CapturingGeminiHandler();
        var external = new ExternalServiceHandler();

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        var seeded = await SeedAsync(scope.ServiceProvider, publicConversation: true);
        gemini.EnqueueStream(ToolCallStream(seeded.ToolName));
        gemini.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Para esa consulta necesita iniciar sesión."}]}}]}

            """);

        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, "Estado de EXP-9"));
        response.EnsureSuccessStatusCode();

        Assert.Empty(external.Requests);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var execution = await vdb.ToolExecutions
            .IgnoreQueryFilters()
            .SingleAsync(e => e.ConversationId == seeded.ConversationId);
        Assert.Equal(ToolExecutionOutcome.PolicyBlocked, execution.Outcome);
        Assert.Equal("auth_required", execution.ErrorCategory);

        var blocked = await vdb.TelemetryEvents
            .IgnoreQueryFilters()
            .SingleAsync(e => e.ConversationId == seeded.ConversationId && e.EventName == "tool_call_blocked");
        Assert.Equal("tool", blocked.Stage);
    }

    [Fact]
    public async Task ExternalFailure_RecordsHttpError_AndModelAnswersFromErrorPayload()
    {
        var gemini = new CapturingGeminiHandler();
        var external = new ExternalServiceHandler
        {
            Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"detail":"missing"}""", Encoding.UTF8, "application/json"),
            }),
        };

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        var seeded = await SeedAsync(scope.ServiceProvider);
        gemini.EnqueueStream(ToolCallStream(seeded.ToolName));
        gemini.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"No se encontró el expediente."}]}}]}

            """);

        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, "Estado de EXP-9"));
        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("No se encontró", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var execution = await vdb.ToolExecutions
            .IgnoreQueryFilters()
            .SingleAsync(e => e.ConversationId == seeded.ConversationId);
        Assert.Equal(ToolExecutionOutcome.HttpError, execution.Outcome);
        Assert.Equal(404, execution.HttpStatus);

        var failed = await vdb.TelemetryEvents
            .IgnoreQueryFilters()
            .SingleAsync(e => e.ConversationId == seeded.ConversationId && e.EventName == "tool_call_failed");
        Assert.NotNull(failed);
    }

    [Fact]
    public async Task ChatTurn_FunctionResponse_IncludesSolicitudAndResultado()
    {
        var gemini = new CapturingGeminiHandler();
        var external = new ExternalServiceHandler();

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        var seeded = await SeedAsync(scope.ServiceProvider);
        gemini.EnqueueStream(ToolCallStream(seeded.ToolName));
        gemini.EnqueueStream(FinalAnswerStream);

        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, "¿Cómo va mi expediente EXP-9?"));
        response.EnsureSuccessStatusCode();

        // The second Gemini request carries the functionResponse: the requested
        // args ride alongside the result so the model grounds its answer on
        // what was actually asked.
        Assert.Equal(2, gemini.RequestBodies.Count);
        var secondBody = gemini.RequestBodies[1];
        Assert.Contains("\"solicitud\"", secondBody, StringComparison.Ordinal);
        Assert.Contains("EXP-9", secondBody, StringComparison.Ordinal);
        Assert.Contains("\"resultado\"", secondBody, StringComparison.Ordinal);
        Assert.Contains("aprobado", secondBody, StringComparison.Ordinal);

        // The persisted Tool-role response row stays raw (audit unchanged).
        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var toolRows = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == seeded.ConversationId && m.Role == MessageRole.Tool)
            .OrderBy(m => m.Id)
            .ToListAsync();
        Assert.Equal(2, toolRows.Count);
        Assert.DoesNotContain("\"solicitud\"", toolRows[1].Content, StringComparison.Ordinal);
        Assert.Contains("aprobado", toolRows[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatTurn_PostToolAnswerPass_IncludesCurrentUserQuestion()
    {
        var gemini = new CapturingGeminiHandler();
        var external = new ExternalServiceHandler();

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        var seeded = await SeedAsync(scope.ServiceProvider);
        gemini.EnqueueStream(ToolCallStream(seeded.ToolName));
        gemini.EnqueueStream(FinalAnswerStream);

        // Distinctive, accent-free phrase that appears in neither the tool args
        // ("EXP-9") nor the tool result ("aprobado"), so finding it in the second
        // (answer-generation) request proves the user's ACTUAL question is present
        // — not merely the solicitud echo of the model-generated args.
        const string question = "Confirma el avance del proceso por favor.";
        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, question));
        response.EnsureSuccessStatusCode();

        Assert.Equal(2, gemini.RequestBodies.Count);
        // Regression guard for commit e42fff8: ChatTurnService must NOT null the
        // current user question after a tool call. Without it the post-tool pass
        // omits the question and the model answers the previous topic.
        Assert.Contains("avance del proceso", gemini.RequestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatTurn_ToolCallThenWhitespaceOnlySecondPass_StreamsFallback()
    {
        var gemini = new CapturingGeminiHandler();
        var external = new ExternalServiceHandler();

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        var seeded = await SeedAsync(scope.ServiceProvider);
        gemini.EnqueueStream(ToolCallStream(seeded.ToolName));
        // Second pass: the model streams only whitespace after the tool result.
        gemini.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"  "}]}}]}

            """);

        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, "Estado de EXP-9"));
        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("FALLBACK_EXT", sse, StringComparison.Ordinal);

        using var verifyScope = app.Services.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var assistant = await vdb.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == seeded.ConversationId && m.Role == MessageRole.Assistant)
            .OrderByDescending(m => m.Id)
            .FirstAsync();
        Assert.Equal("FALLBACK_EXT", assistant.Content);
        Assert.Equal("gemini-fallback", assistant.Provider);
    }

    [Fact]
    public async Task SlowTool_EmitsSseKeepalives_SoTheAgentStreamStaysAlive()
    {
        var gemini = new CapturingGeminiHandler();
        var external = new ExternalServiceHandler
        {
            Responder = async _ =>
            {
                // Keepalive fires after 5s of SSE silence; stay quiet longer so
                // the probe is reliable under parallel CI load.
                await Task.Delay(TimeSpan.FromSeconds(8));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"estado":"aprobado"}""", Encoding.UTF8, "application/json"),
                };
            },
        };

        using var app = BuildApp(_factory, gemini, external);
        using var scope = app.Services.CreateScope();
        // Default 2 s budget would time out; give this tool a longer budget.
        var seeded = await SeedAsync(scope.ServiceProvider,
            configureDefinition: d => d.TimeoutMsOverride = 15000);
        gemini.EnqueueStream(ToolCallStream(seeded.ToolName));
        gemini.EnqueueStream(FinalAnswerStream);

        var response = await AgentClient(app).PostAsync(
            "/livekit/chat/turn", TurnPayload(seeded.ConversationId, "Estado de EXP-9"));
        response.EnsureSuccessStatusCode();
        var sse = await response.Content.ReadAsStringAsync();

        // The silent 6 s tool window produced at least one SSE comment (which
        // the agent parser ignores — no TTS, no state change) and the turn
        // still completed normally.
        Assert.Contains(": keepalive", sse, StringComparison.Ordinal);
        Assert.Contains("está aprobado", sse, StringComparison.Ordinal);
    }
}
