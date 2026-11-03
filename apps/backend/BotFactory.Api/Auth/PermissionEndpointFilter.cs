using BotFactory.Core.Auth;
using BotFactory.Core.Tenancy;

namespace BotFactory.Api.Auth;

public class PermissionEndpointFilter : IEndpointFilter
{
    private readonly string _requiredPermission;

    public PermissionEndpointFilter(string requiredPermission)
    {
        _requiredPermission = requiredPermission;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();

        if (tenantContext.Permissions.Contains(Permissions.SuperAdmin) ||
            tenantContext.Permissions.Contains(Permissions.BotAdmin) ||
            tenantContext.Permissions.Contains(_requiredPermission))
        {
            return await next(context);
        }

        return Results.Forbid();
    }
}

public static class EndpointFilterExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)
    {
        return builder.AddEndpointFilter(new PermissionEndpointFilter(permission));
    }
}
