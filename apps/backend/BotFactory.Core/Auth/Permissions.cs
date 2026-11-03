namespace BotFactory.Core.Auth;

public static class Permissions
{
    public const string SuperAdmin = "superadmin";
    public const string BotAdmin = "bot:admin";
    public const string BotUpdate = "bot:update";
    public const string BotDelete = "bot:delete";
    public const string BotPublish = "bot:publish";
    public const string KbUpdate = "kb:update";
    public const string KbDelete = "kb:delete";
    public const string ConversationRead = "conversation:read";
    public const string TelemetryRead = "telemetry:read";
    public const string AuditRead = "audit:read";
    public const string BotChat = "bot:chat";
    public const string BotRead = "bot:read";

    // Claim-only permission carried by anonymous public-session tokens.
    // Never granted to GOB-ID users; scopes what an anonymous visitor can do
    // (own-conversation chat) without opening any authenticated surface.
    public const string PublicChat = "public:chat";
    public const string SettingsRead = "settings:read";
    public const string SettingsUpdate = "settings:update";

    // External tools (planning/external-tools-plan.md). Defined ahead of GOB-ID
    // delivery: until GOB-ID ships these strings, bot:admin/superadmin pass via
    // the PermissionEndpointFilter override.
    public const string ToolRead = "tool:read";
    public const string ToolUpdate = "tool:update";
    public const string ToolDelete = "tool:delete";
    public const string ToolPublish = "tool:publish";
}
