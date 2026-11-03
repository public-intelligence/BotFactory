namespace BotFactory.Core.Auth;

/// <summary>
/// Claim names/values used by anonymous public-session JWTs. The token is
/// server-signed and stateless: it reuses the standard tenant claim shape
/// (sub / organization_id / permissions) so TenantResolutionMiddleware and
/// ConversationHub.HydrateTenant work unchanged, plus a token_type marker that
/// confines it to the public surface (PublicTokenScopeMiddleware).
/// </summary>
public static class PublicTokenClaims
{
    public const string TokenType = "token_type";
    public const string PublicTokenType = "public";
    public const string BotId = "bot_id";
    public const string BotPublicId = "bot_public_id";

    /// <summary>Prefix for the synthetic anonymous user id stored in Conversation.UserId.</summary>
    public const string AnonymousUserPrefix = "anon:";
}
