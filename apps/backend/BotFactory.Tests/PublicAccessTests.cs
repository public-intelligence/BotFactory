using BotFactory.Core.Auth;
using BotFactory.Core.Entities;
using BotFactory.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;

namespace BotFactory.Tests;

public class PublicIdGeneratorTests
{
    [Fact]
    public void NewId_HasExpectedLengthAndAlphabet()
    {
        var id = PublicIdGenerator.NewId();
        Assert.Equal(PublicIdGenerator.Length, id.Length);
        Assert.Matches("^[0-9a-zA-Z]+$", id);
    }

    [Fact]
    public void NewId_IsUniqueAcrossManyDraws()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => PublicIdGenerator.NewId()).ToHashSet();
        Assert.Equal(1000, ids.Count);
    }
}

public class EmbedOriginsTests
{
    [Theory]
    [InlineData("https://example.gob.do", "https://example.gob.do")]
    [InlineData("https://Example.GOB.do/", "https://example.gob.do")]
    [InlineData("https://example.gob.do:8443", "https://example.gob.do:8443")]
    [InlineData("http://localhost:4200", "http://localhost:4200")]
    public void NormalizeOrigin_AcceptsBareOrigins(string input, string expected)
    {
        Assert.Equal(expected, EmbedOrigins.NormalizeOrigin(input));
    }

    [Theory]
    [InlineData("example.gob.do")]           // no scheme
    [InlineData("ftp://example.gob.do")]     // wrong scheme
    [InlineData("https://example.gob.do/path")]
    [InlineData("https://example.gob.do?q=1")]
    [InlineData("https://user:pw@example.gob.do")]
    [InlineData("javascript:alert(1)")]
    public void NormalizeOrigin_RejectsNonOrigins(string input)
    {
        Assert.Null(EmbedOrigins.NormalizeOrigin(input));
    }

    [Fact]
    public void Parse_SplitsAndDeduplicates()
    {
        var parsed = EmbedOrigins.Parse("https://a.gob.do,\nhttps://b.gob.do\nhttps://a.gob.do");
        Assert.Equal(["https://a.gob.do", "https://b.gob.do"], parsed);
    }

    [Fact]
    public void Normalize_ProducesCommaSeparatedStorageForm()
    {
        Assert.Equal("https://a.gob.do,https://b.gob.do",
            EmbedOrigins.Normalize("https://a.gob.do/\n https://B.gob.do "));
    }

    [Fact]
    public void InvalidEntries_ReportsWhatWouldBeDropped()
    {
        var invalid = EmbedOrigins.InvalidEntries("https://ok.gob.do,not-an-origin");
        Assert.Equal(["not-an-origin"], invalid);
    }
}

public class PublicTokenServiceTests
{
    private static PublicTokenService CreateService(int ttlMinutes = 15) =>
        new(Options.Create(new PublicAccessOptions
        {
            SigningKey = "test-public-signing-key-at-least-32-chars!!",
            TokenTtlMinutes = ttlMinutes,
        }));

    private static Bot PublicBot() => new()
    {
        Id = 42,
        OrganizationId = "7",
        PublicId = "abc123xyz",
        RequiresAuthentication = false,
        PublishedState = BotStates.Published,
    };

    [Fact]
    public void MintToken_CarriesTenantShapeAndPublicMarkers()
    {
        var service = CreateService();
        var anonId = PublicTokenService.NewAnonymousUserId();

        var result = service.MintToken(PublicBot(), anonId);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal(anonId, jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("7", jwt.Claims.Single(c => c.Type == "organization_id").Value);
        Assert.Equal(PublicTokenClaims.PublicTokenType,
            jwt.Claims.Single(c => c.Type == PublicTokenClaims.TokenType).Value);
        Assert.Contains(Permissions.PublicChat,
            jwt.Claims.Single(c => c.Type == "permissions").Value);
        Assert.Equal("42", jwt.Claims.Single(c => c.Type == PublicTokenClaims.BotId).Value);
    }

    [Fact]
    public void MintToken_HonorsConfiguredTtl()
    {
        var service = CreateService(ttlMinutes: 15);
        var result = service.MintToken(PublicBot(), PublicTokenService.NewAnonymousUserId());

        var remaining = result.ExpiresAt - DateTime.UtcNow;
        Assert.InRange(remaining.TotalMinutes, 13, 16);
    }

    [Fact]
    public void NewAnonymousUserId_UsesAnonPrefix()
    {
        var id = PublicTokenService.NewAnonymousUserId();
        Assert.StartsWith(PublicTokenClaims.AnonymousUserPrefix, id);
        Assert.True(id.Length > PublicTokenClaims.AnonymousUserPrefix.Length + 20);
    }
}
