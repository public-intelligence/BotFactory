namespace BotFactory.Core.Services;

/// <summary>
/// Encrypts and decrypts sensitive values (API keys).
/// Initial implementation: AES-256-CBC with master key from environment variable.
/// NOTE: We chose simple AES initially. Migrating to ASP.NET Data Protection
/// would give us key rotation for free — the interface is designed for easy swap.
/// For now, key rotation is not implemented — plan to revisit before production.
/// </summary>
public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
