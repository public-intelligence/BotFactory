using System.IdentityModel.Tokens.Jwt;
using BotFactory.Core.Auth;
using BotFactory.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BotFactory.Tests;

public class FakeAuthProviderTests
{
    private static FakeAuthProvider CreateProvider() =>
        new(Options.Create(new FakeIdOptions
        {
            Issuer = "bot-factory-dev",
            Audience = "bot-factory",
            SigningKey = "dev-signing-key-at-least-32-chars-long!!",
        }), NullLogger<FakeAuthProvider>.Instance);

    [Fact]
    public async Task ExchangeCode_KnownEmail_IssuesJwtWithClaims()
    {
        var provider = CreateProvider();
        var result = await provider.ExchangeCodeAsync("superadmin@example.com", "http://localhost/cb", 7);

        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.Equal("00000000-0000-0000-0000-000000000001", jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("7", jwt.Claims.Single(c => c.Type == "organization_id").Value);
        Assert.Equal("Super Administrador", jwt.Claims.Single(c => c.Type == "person_full_name").Value);
        Assert.Contains("superadmin", jwt.Claims.Single(c => c.Type == "permissions").Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExchangeCode_UnknownEmail_Throws()
    {
        var provider = CreateProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ExchangeCodeAsync("nobody@example.com", "http://localhost/cb", 1));
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndRejectsReuse()
    {
        var provider = CreateProvider();
        var issued = await provider.ExchangeCodeAsync("admin@example.com", "http://localhost/cb", 1);

        var refreshed = await provider.RefreshAsync(issued.RefreshToken!);
        // Access JWT may match if minted in the same second with identical claims;
        // refresh tokens must rotate and the old one must be one-time-use.
        Assert.NotEqual(issued.RefreshToken, refreshed.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.RefreshAsync(issued.RefreshToken!));
    }

    [Fact]
    public async Task Refresh_InvalidToken_Throws()
    {
        var provider = CreateProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.RefreshAsync("not-a-real-refresh-token"));
    }

    [Fact]
    public void GetLoginUrl_EncodesRedirectUri()
    {
        var provider = CreateProvider();
        var url = provider.GetLoginUrl("http://localhost:4200/auth/callback?x=1");
        Assert.StartsWith("/api/auth/dev/login?redirectUri=", url);
        Assert.Contains(Uri.EscapeDataString("http://localhost:4200/auth/callback?x=1"), url);
    }

    [Fact]
    public void GetSeededUserSummaries_ReturnsSevenUsers()
    {
        var users = FakeAuthProvider.GetSeededUserSummaries();
        Assert.Equal(7, users.Count);
        Assert.Contains(users, u => u.Email == "superadmin@example.com");
    }
}
