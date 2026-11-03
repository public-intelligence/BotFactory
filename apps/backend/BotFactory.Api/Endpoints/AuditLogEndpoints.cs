using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;

namespace BotFactory.Api.Endpoints;

public static class AuditLogEndpoints
{
    public static void MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/audit-logs")
            .RequireAuthorization()
            .WithTags("AuditLogs");

        group.MapGet("/", async (
            IAuditLogService auditLogService,
            string? entityType = null,
            int? entityId = null,
            DateTime? from = null,
            DateTime? to = null,
            int page = 1,
            int pageSize = 20) =>
        {
            var (items, totalCount) = await auditLogService.GetLogsAsync(entityType, entityId, from, to, page, pageSize);
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            return Results.Ok(new PaginatedResponse<AuditLogResponse>(
                items.Select(ToResponse).ToList(),
                page, pageSize, totalCount, totalPages));
        })
        .RequirePermission(Permissions.AuditRead)
        .WithName("GetAuditLogs")
        .Produces<PaginatedResponse<AuditLogResponse>>();
    }

    private static AuditLogResponse ToResponse(AuditLog log) => new(
        log.Id, log.EntityType, log.EntityId, log.Action,
        log.Changes, log.UserId, log.UserName, log.CreatedAt);
}
