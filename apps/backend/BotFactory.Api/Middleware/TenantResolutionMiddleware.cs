using System.Text.Json;
using BotFactory.Infrastructure.Tenancy;

namespace BotFactory.Api.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var orgClaim = context.User.FindFirst("organization_id")?.Value;
            var subClaim = context.User.FindFirst("sub")?.Value;
            var permClaim = context.User.FindFirst("permissions")?.Value;
            var fullNameClaim = context.User.FindFirst("person_full_name")?.Value;
            var orgNameClaim = context.User.FindFirst("organization_name")?.Value;

            tenantContext.OrganizationId = orgClaim ?? string.Empty;
            tenantContext.UserId = subClaim ?? string.Empty;
            tenantContext.FullName = fullNameClaim ?? string.Empty;
            tenantContext.OrganizationName = orgNameClaim ?? string.Empty;

            if (!string.IsNullOrEmpty(permClaim))
            {
                try
                {
                    tenantContext.Permissions = JsonSerializer.Deserialize<List<string>>(permClaim) ?? [];
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "TenantResolution: failed to deserialize permissions claim for userId={UserId}. Raw value: {PermClaim}",
                        subClaim, permClaim);
                    tenantContext.Permissions = [];
                }
            }

        }

        await _next(context);
    }
}
