using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BotFactory.Core.Auth;
using BotFactory.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BotFactory.Infrastructure.Auth;

public record PublicTokenResult(string Token, DateTime ExpiresAt);

/// <summary>
/// Mints the stateless, server-signed JWT that authenticates an anonymous
/// public-bot session (no Sessions row, no cookie — the SPA holds it in memory
/// and presents it as a Bearer token / hub access_token). Mirrors the claim
/// shape produced by the identity providers so the existing tenant plumbing
/// (TenantResolutionMiddleware, ConversationHub.HydrateTenant) needs no changes.
/// </summary>
public class PublicTokenService
{
    private readonly IOptions<PublicAccessOptions> _options;

    public PublicTokenService(IOptions<PublicAccessOptions> options)
    {
        _options = options;
    }

    public static string NewAnonymousUserId() =>
        PublicTokenClaims.AnonymousUserPrefix + Guid.NewGuid().ToString("N");

    public PublicTokenResult MintToken(Bot bot, string anonymousUserId)
    {
        var options = _options.Value;
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, options.TokenTtlMinutes));

        var claims = new List<Claim>
        {
            new("sub", anonymousUserId),
            new("organization_id", bot.OrganizationId),
            new("person_full_name", "Visitante"),
            new("permissions", JsonSerializer.Serialize(new[] { Permissions.PublicChat })),
            new(PublicTokenClaims.TokenType, PublicTokenClaims.PublicTokenType),
            new(PublicTokenClaims.BotId, bot.Id.ToString()),
            new(PublicTokenClaims.BotPublicId, bot.PublicId),
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: signingCredentials);

        return new PublicTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
