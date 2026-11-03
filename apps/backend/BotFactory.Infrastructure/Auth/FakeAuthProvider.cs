using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BotFactory.Core.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BotFactory.Infrastructure.Auth;

public class FakeAuthProvider : IAuthProvider
{
    private readonly FakeIdOptions _options;
    private readonly ILogger<FakeAuthProvider> _logger;
    private readonly ConcurrentDictionary<string, (string Email, int OrgAppId)> _refreshTokenStore = new();

    private static readonly List<FakeUser> SeededUsers =
    [
        new("00000000-0000-0000-0000-000000000001", "superadmin@example.com", "Super Administrador", "1", "Administrador Maestro", "Demo",
            [Permissions.SuperAdmin, Permissions.BotAdmin, Permissions.BotUpdate, Permissions.BotDelete,
             Permissions.BotPublish, Permissions.KbUpdate, Permissions.KbDelete, Permissions.ConversationRead,
             Permissions.TelemetryRead, Permissions.AuditRead, Permissions.BotChat, Permissions.BotRead]),

        new("00000000-0000-0000-0000-000000000002", "admin@example.com", "Administrador", "1", "Administrador", "Demo",
            [Permissions.BotAdmin, Permissions.BotUpdate, Permissions.BotDelete, Permissions.BotPublish,
             Permissions.KbUpdate, Permissions.KbDelete, Permissions.ConversationRead, Permissions.TelemetryRead,
             Permissions.AuditRead, Permissions.BotChat, Permissions.BotRead]),

        new("00000000-0000-0000-0000-000000000003", "botcreator@example.com", "Creador de Bots", "1", "Creador de Bots", "Demo",
            [Permissions.BotRead, Permissions.BotUpdate, Permissions.BotPublish, Permissions.KbUpdate,
             Permissions.KbDelete, Permissions.ConversationRead, Permissions.TelemetryRead, Permissions.BotChat]),

        new("00000000-0000-0000-0000-000000000004", "kbeditor@example.com", "Editor de Conocimiento", "1", "Editor de Conocimiento", "Demo",
            [Permissions.BotRead, Permissions.KbUpdate, Permissions.KbDelete, Permissions.BotChat]),

        new("00000000-0000-0000-0000-000000000005", "analyst@example.com", "Analista", "1", "Analista", "Demo",
            [Permissions.BotRead, Permissions.ConversationRead, Permissions.TelemetryRead, Permissions.BotChat]),

        new("00000000-0000-0000-0000-000000000006", "auditor@example.com", "Auditor", "1", "Auditor", "Demo",
            [Permissions.BotRead, Permissions.ConversationRead, Permissions.AuditRead, Permissions.BotChat]),

        new("00000000-0000-0000-0000-000000000007", "public@example.com", "Usuario Publico", "1", "Usuario Publico", "Demo",
            [Permissions.BotChat]),
    ];

    public FakeAuthProvider(IOptions<FakeIdOptions> options, ILogger<FakeAuthProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _logger.LogInformation("FakeAuthProvider initialized with {UserCount} seeded users", SeededUsers.Count);
    }

    public Task<TokenResult> ExchangeCodeAsync(string code, string redirectUri, int organizationApplicationId)
    {
        var email = code;
        var user = SeededUsers.FirstOrDefault(u => u.Email == email)
            ?? throw new InvalidOperationException($"Unknown fake user: {email}");

        var accessToken = GenerateJwt(user, organizationApplicationId);
        var refreshToken = Guid.NewGuid().ToString("N");
        _refreshTokenStore[refreshToken] = (user.Email, organizationApplicationId);

        _logger.LogInformation("FakeAuth ExchangeCode: issued token for {Email}, orgAppId={OrgAppId}", email, organizationApplicationId);
        return Task.FromResult(new TokenResult(accessToken, refreshToken));
    }

    public Task<TokenResult> RefreshAsync(string refreshToken)
    {
        if (!_refreshTokenStore.TryRemove(refreshToken, out var stored))
            throw new InvalidOperationException("Invalid or expired refresh token");

        var user = SeededUsers.First(u => u.Email == stored.Email);
        var newAccessToken = GenerateJwt(user, stored.OrgAppId);
        var newRefreshToken = Guid.NewGuid().ToString("N");
        _refreshTokenStore[newRefreshToken] = stored;

        _logger.LogInformation("FakeAuth Refresh: refreshed token for {Email}", stored.Email);
        return Task.FromResult(new TokenResult(newAccessToken, newRefreshToken));
    }

    public string GetLoginUrl(string redirectUri)
    {
        return $"/api/auth/dev/login?redirectUri={Uri.EscapeDataString(redirectUri)}";
    }

    private string GenerateJwt(FakeUser user, int organizationApplicationId)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // Use organizationApplicationId as organization_id so tenant isolation works in tests
        var claims = new List<Claim>
        {
            new("sub", user.Id),
            new("organization_id", organizationApplicationId.ToString()),
            new("organization_name", user.OrganizationName),
            new("person_full_name", user.FullName),
            new("permissions", JsonSerializer.Serialize(user.Permissions)),
            new("personnel_id", user.Id),
            new("application_id", "fake-app"),
            new("organization_application_id", organizationApplicationId.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private record FakeUser(
        string Id,
        string Email,
        string FullName,
        string OrganizationId,
        string Role,
        string OrganizationName,
        List<string> Permissions);

    public static IReadOnlyList<(string Email, string FullName, string Role)> GetSeededUserSummaries()
        => SeededUsers.Select(u => (u.Email, u.FullName, u.Role)).ToList();
}
