using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Tools;
using BotFactory.Infrastructure.Tools.Execution;
using BotFactory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BotFactory.Tests;

[Collection("Database")]
public class ToolExecutionGatewayTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public ToolExecutionGatewayTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private sealed class StubConnector : IToolConnector
    {
        public ToolConnectorType Type => ToolConnectorType.Rest;
        public int InvokeCount { get; private set; }
        public Func<ToolConnectorRequest, ToolConnectorResult>? Responder { get; set; }

        public Task<ToolConnectorResult> InvokeAsync(ToolConnectorRequest request, CancellationToken cancellationToken)
        {
            InvokeCount++;
            return Task.FromResult(Responder?.Invoke(request) ?? SuccessResult());
        }

        private static ToolConnectorResult SuccessResult() => new(
            ToolExecutionOutcome.Success,
            200,
            JsonDocument.Parse("""{"ok":true}""").RootElement,
            12,
            0,
            "GET https://api.example.test/ok");
    }

    private sealed class ThrowingCredentialProvider : IToolCredentialProvider
    {
        public ToolCredentials Resolve(ToolConnection connection) =>
            throw new InvalidOperationException("decrypt failed");
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static ResolvedBotTool PublishedTool(int defId = 1, int connId = 1) => new(
        new ToolDefinition
        {
            Id = defId,
            Name = "consultar",
            DisplayName = "Consultar",
            HttpMethod = "GET",
            PathTemplate = "/items/{id}",
            ParametersSchemaJson =
                """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"]}""",
            Status = ToolDefinitionStatus.Published,
            RequiresAuthenticatedUser = false,
        },
        new ToolConnection
        {
            Id = connId,
            Name = "conn",
            ConnectorType = ToolConnectorType.Rest,
            BaseUrl = "https://api.example.test",
            Status = ToolConnectionStatus.Active,
        },
        new BotTool { Enabled = true });

    private static ToolContext Context(int conversationId = 500) => new(
        new Bot { Id = 1, BotName = "b", UrlSlug = "b", MasterPrompt = "x" },
        new Conversation
        {
            Id = conversationId,
            BotId = 1,
            UserId = "u1",
            AccessMode = ConversationAccessModes.Authenticated,
        },
        TurnId: Guid.NewGuid());

    private ToolExecutionGateway BuildGateway(
        BotFactoryDbContext db,
        StubConnector connector,
        IToolCredentialProvider? credentials = null) =>
        new(
            db,
            [connector],
            new ToolArgumentValidator(),
            new ToolPolicyEvaluator(db),
            credentials ?? new ToolCredentialProvider(_factory.Services.CreateScope().ServiceProvider.GetRequiredService<IEncryptionService>()),
            NullLogger<ToolExecutionGateway>.Instance);

    [Fact]
    public async Task ExecuteAsync_ValidationFailure_DoesNotInvokeConnector()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var connector = new StubConnector();
        var gateway = BuildGateway(db, connector);

        var invocation = await gateway.ExecuteAsync(
            PublishedTool(),
            Args("{}"),
            Context(),
            CancellationToken.None);

        Assert.Equal(0, connector.InvokeCount);
        var execution = await db.ToolExecutions.OrderByDescending(e => e.Id).FirstAsync();
        Assert.Equal(ToolExecutionOutcome.ValidationFailed, execution.Outcome);
        Assert.Contains("validation_failed", invocation.ResponsePayload!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_PolicyBlocked_DoesNotInvokeConnector()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var connector = new StubConnector();
        var gateway = BuildGateway(db, connector);
        var tool = PublishedTool();
        tool.Definition.Status = ToolDefinitionStatus.Draft;

        var invocation = await gateway.ExecuteAsync(
            tool,
            Args("""{"id":"1"}"""),
            Context(),
            CancellationToken.None);

        Assert.Equal(0, connector.InvokeCount);
        var execution = await db.ToolExecutions.OrderByDescending(e => e.Id).FirstAsync();
        Assert.Equal(ToolExecutionOutcome.PolicyBlocked, execution.Outcome);
        Assert.Contains("blocked", invocation.ResponsePayload!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Success_RecordsExecutionAndReturnsDisplay()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var connector = new StubConnector();
        var gateway = BuildGateway(db, connector);

        var invocation = await gateway.ExecuteAsync(
            PublishedTool(),
            Args("""{"id":"42"}"""),
            Context(),
            CancellationToken.None);

        Assert.Equal(1, connector.InvokeCount);
        var execution = await db.ToolExecutions.OrderByDescending(e => e.Id).FirstAsync();
        Assert.Equal(ToolExecutionOutcome.Success, execution.Outcome);
        Assert.NotEmpty(invocation.Display);
        Assert.Equal("tool_call_completed", invocation.Telemetry?.EventName);
    }

    [Fact]
    public async Task ExecuteAsync_MissingConnector_ReturnsConnectorError()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var gateway = new ToolExecutionGateway(
            db,
            [],
            new ToolArgumentValidator(),
            new ToolPolicyEvaluator(db),
            new ToolCredentialProvider(scope.ServiceProvider.GetRequiredService<IEncryptionService>()),
            NullLogger<ToolExecutionGateway>.Instance);

        var tool = PublishedTool();
        tool.Connection.ConnectorType = ToolConnectorType.Rest;

        var invocation = await gateway.ExecuteAsync(
            tool,
            Args("""{"id":"1"}"""),
            Context(),
            CancellationToken.None);

        var execution = await db.ToolExecutions.OrderByDescending(e => e.Id).FirstAsync();
        Assert.Equal(ToolExecutionOutcome.ConnectorError, execution.Outcome);
        Assert.Equal("connector_missing", execution.ErrorCategory);
        Assert.Contains("tool_call_failed", invocation.Telemetry?.EventName);
    }

    [Fact]
    public async Task ExecuteAsync_CredentialFailure_ReturnsConnectorError()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var connector = new StubConnector();
        var gateway = BuildGateway(db, connector, new ThrowingCredentialProvider());
        var tool = PublishedTool();
        tool.Connection.EncryptedSecretsJson = "present";

        var invocation = await gateway.ExecuteAsync(
            tool,
            Args("""{"id":"1"}"""),
            Context(),
            CancellationToken.None);

        Assert.Equal(0, connector.InvokeCount);
        var execution = await db.ToolExecutions.OrderByDescending(e => e.Id).FirstAsync();
        Assert.Equal(ToolExecutionOutcome.ConnectorError, execution.Outcome);
        Assert.Equal("credentials_error", execution.ErrorCategory);
        Assert.Contains("tool_call_failed", invocation.Telemetry?.EventName);
    }

    [Fact]
    public async Task ExecuteTestAsync_SkipsConversationPolicy_AllowsDraft()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var connector = new StubConnector();
        var gateway = BuildGateway(db, connector);
        var tool = PublishedTool();
        tool.Definition.Status = ToolDefinitionStatus.Draft;

        var result = await gateway.ExecuteTestAsync(
            tool.Definition,
            tool.Connection,
            Args("""{"id":"9"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.Success, result.Outcome);
        Assert.True(await db.ToolExecutions.AnyAsync(e => e.IsTest));
    }

    [Fact]
    public async Task ExecuteTestAsync_ConsequentialTool_Blocked()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var connector = new StubConnector();
        var gateway = BuildGateway(db, connector);
        var tool = PublishedTool();
        tool.Definition.IsConsequential = true;

        var result = await gateway.ExecuteTestAsync(
            tool.Definition,
            tool.Connection,
            Args("""{"id":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.PolicyBlocked, result.Outcome);
        Assert.Equal("consequential_not_supported", result.ErrorCategory);
        Assert.Equal(0, connector.InvokeCount);
    }
}
