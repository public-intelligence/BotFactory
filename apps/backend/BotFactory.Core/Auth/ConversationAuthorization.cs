using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;

namespace BotFactory.Core.Auth;

/// <summary>
/// Who may read an active or historical conversation thread.
/// </summary>
public static class ConversationAuthorization
{
    public static bool CanView(Conversation conversation, ITenantContext tenant)
    {
        if (!string.Equals(conversation.OrganizationId, tenant.OrganizationId, StringComparison.Ordinal))
            return false;

        // Owner path: GOB-ID users carry bot:chat; anonymous public sessions
        // carry public:chat (claim-only, scoped to their own conversation).
        if (string.Equals(conversation.UserId, tenant.UserId, StringComparison.Ordinal)
            && (tenant.Has(Permissions.BotChat) || tenant.Has(Permissions.PublicChat)))
        {
            return true;
        }

        return tenant.Has(Permissions.SuperAdmin)
               || tenant.Has(Permissions.BotAdmin)
               || tenant.Has(Permissions.BotRead)
               || tenant.Has(Permissions.ConversationRead)
               || tenant.Has(Permissions.TelemetryRead);
    }
}
