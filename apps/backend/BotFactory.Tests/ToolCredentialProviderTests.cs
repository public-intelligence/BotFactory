using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Tools.Execution;
using BotFactory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class ToolCredentialProviderTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public ToolCredentialProviderTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void Resolve_EmptySecrets_ReturnsEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = new ToolCredentialProvider(scope.ServiceProvider.GetRequiredService<IEncryptionService>());

        var creds = provider.Resolve(new ToolConnection { EncryptedSecretsJson = null });

        Assert.Same(ToolCredentials.Empty, creds);
    }

    [Fact]
    public void Resolve_EncryptedJson_ReturnsDictionary()
    {
        using var scope = _factory.Services.CreateScope();
        var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        var provider = new ToolCredentialProvider(encryption);

        var connection = new ToolConnection
        {
            EncryptedSecretsJson = encryption.Encrypt("""{"apiKey":"sekret123","token":"abc"}"""),
        };

        var creds = provider.Resolve(connection);

        Assert.Equal("sekret123", creds.Secrets["apiKey"]);
        Assert.Equal("abc", creds.Secrets["token"]);
    }

    [Fact]
    public void Resolve_InvalidCiphertext_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = new ToolCredentialProvider(scope.ServiceProvider.GetRequiredService<IEncryptionService>());

        var connection = new ToolConnection { EncryptedSecretsJson = "not-valid-ciphertext" };

        Assert.ThrowsAny<Exception>(() => provider.Resolve(connection));
    }
}
