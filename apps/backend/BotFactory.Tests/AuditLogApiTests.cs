using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using BotFactory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class AuditLogApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public AuditLogApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    private static async Task SeedAuditLogAsync(BotFactoryDbContext db, string entityType, int entityId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = "Updated",
            UserId = "user-1",
            UserName = "Test User",
            Changes = [new AuditFieldChange { Field = "Name", OldValue = "old", NewValue = "new" }],
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Auditor_CanListAuditLogs()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
            var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
            await SeedAuditLogAsync(db, "Bot", 42);
        }

        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "auditor@example.com");
        var response = await client.GetFromJsonAsync<PaginatedResponse<AuditLogResponse>>("/api/audit-logs?pageSize=50");

        Assert.NotNull(response);
        Assert.Contains(response.Items, l => l.EntityType == "Bot" && l.EntityId == 42);
    }

    [Fact]
    public async Task Analyst_WithoutAuditRead_Forbidden()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, "analyst@example.com");
        var response = await client.GetAsync("/api/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanFilterByEntityType()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(DbSeeder.SuperAdminOrgId);
            var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
            await SeedAuditLogAsync(db, "Bot", 1);
            await SeedAuditLogAsync(db, "ToolDefinition", 2);
        }

        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);
        var response = await client.GetFromJsonAsync<PaginatedResponse<AuditLogResponse>>(
            "/api/audit-logs?entityType=ToolDefinition");

        Assert.NotNull(response);
        Assert.All(response.Items, i => Assert.Equal("ToolDefinition", i.EntityType));
    }
}
