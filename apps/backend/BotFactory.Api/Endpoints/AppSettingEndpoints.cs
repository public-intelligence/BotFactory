using BotFactory.Api.Auth;
using BotFactory.Core.Auth;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Services;

namespace BotFactory.Api.Endpoints;

public static class AppSettingEndpoints
{
    public static void MapAppSettingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings")
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("/", async (IAppSettingService service) =>
        {
            var settings = await service.GetAllAsync();
            return Results.Ok(settings.Select(ToResponse).ToList());
        })
        .RequirePermission(Permissions.SettingsRead)
        .WithName("GetAppSettings")
        .Produces<List<AppSettingResponse>>();

        group.MapPut("/", async (
            BulkUpsertAppSettingsRequest request,
            IAppSettingService service,
            KbIndexingOrchestrator orchestrator,
            ITenantContext tenantContext,
            ILogger<AppSetting> logger) =>
        {
            var parsed = new List<(ServiceType ServiceType, string ApiKey)>();
            foreach (var s in request.Settings)
            {
                if (!Enum.TryParse<ServiceType>(s.ServiceType, true, out var serviceType))
                    return Results.BadRequest(new { error = $"Invalid service type: {s.ServiceType}" });
                if (string.IsNullOrWhiteSpace(s.ApiKey))
                    return Results.BadRequest(new { error = $"API key is required for service type: {s.ServiceType}" });
                parsed.Add((serviceType, s.ApiKey));
            }

            var results = await service.BulkUpsertAsync(parsed);
            logger.LogInformation("App settings updated for {Count} service type(s)", results.Count);

            // If an embeddings key was just added/updated, process any pending documents
            if (parsed.Any(p => p.ServiceType == ServiceType.Embeddings))
                await orchestrator.EnqueuePendingDocumentsAsync(tenantContext.OrganizationId);

            return Results.Ok(results.Select(ToResponse).ToList());
        })
        .RequirePermission(Permissions.SettingsUpdate)
        .WithName("BulkUpsertAppSettings")
        .Produces<List<AppSettingResponse>>();

        group.MapDelete("/{serviceType}", async (string serviceType, IAppSettingService service, ILogger<AppSetting> logger) =>
        {
            if (!Enum.TryParse<ServiceType>(serviceType, true, out var parsed))
                return Results.BadRequest(new { error = $"Invalid service type: {serviceType}" });

            var deleted = await service.DeleteAsync(parsed);
            if (deleted)
                logger.LogInformation("App setting deleted for service type {ServiceType}", serviceType);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .RequirePermission(Permissions.SettingsUpdate)
        .WithName("DeleteAppSetting")
        .Produces(204)
        .Produces(404);
    }

    private static AppSettingResponse ToResponse(AppSetting s) => new(
        s.Id, s.ServiceType.ToString(), s.Provider.ToString(), s.KeyPrefix,
        !string.IsNullOrEmpty(s.EncryptedApiKey), s.CreatedAt, s.UpdatedAt);
}
