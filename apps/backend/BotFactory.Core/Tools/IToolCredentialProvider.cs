using BotFactory.Core.Entities;

namespace BotFactory.Core.Tools;

/// <summary>
/// Decrypted secret bundle for one connection, materialized only inside the
/// execution gateway at call time. Values must never be logged, serialized
/// into DTOs/telemetry, or placed in LiveKit room metadata.
/// </summary>
public record ToolCredentials(IReadOnlyDictionary<string, string> Secrets)
{
    public static readonly ToolCredentials Empty =
        new(new Dictionary<string, string>());

    public string? Get(string key) =>
        Secrets.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;
}

/// <summary>
/// Seam over secret storage. v1 decrypts ToolConnection.EncryptedSecretsJson
/// via the existing IEncryptionService (storage unchanged per review); a Key
/// Vault-backed implementation could replace this without touching the gateway.
/// </summary>
public interface IToolCredentialProvider
{
    ToolCredentials Resolve(ToolConnection connection);
}
