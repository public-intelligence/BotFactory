using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Tools.Execution;
using BotFactory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class ToolPolicyEvaluatorTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public ToolPolicyEvaluatorTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private static ResolvedBotTool Tool(
        ToolDefinitionStatus defStatus = ToolDefinitionStatus.Published,
        ToolConnectionStatus connStatus = ToolConnectionStatus.Active,
        bool consequential = false,
        bool requiresAuth = true,
        int? maxCalls = null,
        int? overrideMaxCalls = null)
    {
        var connection = new ToolConnection
        {
            Id = 10,
            Name = "conn",
            BaseUrl = "https://api.example.test",
            Status = connStatus,
        };
        var definition = new ToolDefinition
        {
            Id = 20,
            Name = "consultar",
            DisplayName = "Consultar",
            Status = defStatus,
            IsConsequential = consequential,
            RequiresAuthenticatedUser = requiresAuth,
            MaxCallsPerConversation = maxCalls,
        };
        var association = new BotTool
        {
            MaxCallsPerConversationOverride = overrideMaxCalls,
        };
        return new ResolvedBotTool(definition, connection, association);
    }

    private static ToolContext Context(bool publicConversation = false) => new(
        new Bot { Id = 1, BotName = "b", UrlSlug = "b", MasterPrompt = "x", TopK = 5, SimilarityThreshold = 0.9 },
        new Conversation
        {
            Id = 100,
            BotId = 1,
            UserId = "u1",
            AccessMode = publicConversation
                ? ConversationAccessModes.Public
                : ConversationAccessModes.Authenticated,
        },
        TurnId: Guid.NewGuid());

    [Fact]
    public async Task EvaluateAsync_PublishedActiveReadOnly_Allows()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var evaluator = new ToolPolicyEvaluator(scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>());

        var decision = await evaluator.EvaluateAsync(Tool(requiresAuth: false), Context(), CancellationToken.None);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task EvaluateAsync_DraftTool_DeniesNotPublished()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var evaluator = new ToolPolicyEvaluator(scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>());

        var decision = await evaluator.EvaluateAsync(
            Tool(defStatus: ToolDefinitionStatus.Draft, requiresAuth: false),
            Context(),
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("tool_not_published", decision.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_DisabledConnection_Denies()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var evaluator = new ToolPolicyEvaluator(scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>());

        var decision = await evaluator.EvaluateAsync(
            Tool(connStatus: ToolConnectionStatus.Disabled, requiresAuth: false),
            Context(),
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("connection_disabled", decision.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_Consequential_Denies()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var evaluator = new ToolPolicyEvaluator(scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>());

        var decision = await evaluator.EvaluateAsync(
            Tool(consequential: true, requiresAuth: false),
            Context(),
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("consequential_not_supported", decision.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_AuthRequiredOnPublicConversation_Denies()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var evaluator = new ToolPolicyEvaluator(scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>());

        var decision = await evaluator.EvaluateAsync(Tool(), Context(publicConversation: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("auth_required", decision.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_MaxCallsReached_Denies()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tool = Tool(maxCalls: 1, requiresAuth: false);
        var ctx = Context();

        db.ToolExecutions.Add(new ToolExecution
        {
            ConversationId = ctx.Conversation.Id,
            ToolDefinitionId = tool.Definition.Id,
            ToolConnectionId = tool.Connection.Id,
            ToolName = tool.Definition.Name,
            ConnectorType = ToolConnectorType.Rest,
            Outcome = ToolExecutionOutcome.Success,
            ResolvedRequestSummary = "GET ok",
            LatencyMs = 1,
        });
        await db.SaveChangesAsync();

        var evaluator = new ToolPolicyEvaluator(db);
        var decision = await evaluator.EvaluateAsync(tool, ctx, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("max_calls_per_conversation", decision.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_PolicyBlockedAttempts_DoNotConsumeCap()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tool = Tool(maxCalls: 1, requiresAuth: false);
        var ctx = Context();

        db.ToolExecutions.Add(new ToolExecution
        {
            ConversationId = ctx.Conversation.Id,
            ToolDefinitionId = tool.Definition.Id,
            ToolConnectionId = tool.Connection.Id,
            ToolName = tool.Definition.Name,
            ConnectorType = ToolConnectorType.Rest,
            Outcome = ToolExecutionOutcome.PolicyBlocked,
            ResolvedRequestSummary = "blocked",
            LatencyMs = 0,
        });
        await db.SaveChangesAsync();

        var evaluator = new ToolPolicyEvaluator(db);
        var decision = await evaluator.EvaluateAsync(tool, ctx, CancellationToken.None);

        Assert.True(decision.Allowed);
    }
}
