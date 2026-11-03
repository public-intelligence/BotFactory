using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tools;

namespace BotFactory.Infrastructure.Tools.Execution;

/// <summary>
/// Decrypts ToolConnection.EncryptedSecretsJson via the platform
/// IEncryptionService (same AES pattern as AppSetting API keys — storage
/// unchanged per review). Secrets exist in memory only for the duration of one
/// gateway execution.
/// </summary>
public class ToolCredentialProvider : IToolCredentialProvider
{
    private readonly IEncryptionService _encryption;

    public ToolCredentialProvider(IEncryptionService encryption)
    {
        _encryption = encryption;
    }

    public ToolCredentials Resolve(ToolConnection connection)
    {
        if (string.IsNullOrEmpty(connection.EncryptedSecretsJson))
            return ToolCredentials.Empty;

        var json = _encryption.Decrypt(connection.EncryptedSecretsJson);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();

        return new ToolCredentials(dict);
    }
}
