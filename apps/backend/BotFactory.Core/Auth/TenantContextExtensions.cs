using BotFactory.Core.Tenancy;

namespace BotFactory.Core.Auth;

/// <summary>
/// Convenience predicates over <see cref="ITenantContext.Permissions"/> to keep
/// permission policy in one place instead of scattered string checks.
/// </summary>
public static class TenantContextExtensions
{
    /// <summary>
    /// Permissions that authorize previewing non-published bots for testing.
    /// Only publishers (and admins) need draft access; a <c>bot:read</c> analyst
    /// can still list drafts in the admin catalog but cannot run them from the
    /// public chat URL until someone with <c>bot:publish</c> ships them.
    /// </summary>
    private static readonly string[] DraftVisiblePermissions =
    [
        Permissions.SuperAdmin,
        Permissions.BotAdmin,
        Permissions.BotPublish,
    ];

    /// <summary>
    /// Returns true when the caller is allowed to preview non-published bots
    /// (admins and publishers) — typically to test them from the public chat
    /// URL or to start a chat session against a draft.
    /// </summary>
    public static bool CanViewDraftBots(this ITenantContext tenantContext)
    {
        foreach (var perm in DraftVisiblePermissions)
        {
            if (tenantContext.Permissions.Contains(perm)) return true;
        }
        return false;
    }

    public static bool Has(this ITenantContext tenantContext, string permission) =>
        tenantContext.Permissions.Contains(permission);
}
