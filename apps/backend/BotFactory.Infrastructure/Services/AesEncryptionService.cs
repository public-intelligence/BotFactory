using System.Security.Cryptography;
using BotFactory.Core.Services;
using Microsoft.Extensions.Options;

namespace BotFactory.Infrastructure.Services;

/// <summary>
/// AES-256-CBC encryption with a master key from configuration.
/// NOTE: We chose simple AES initially. Migrating to ASP.NET Data Protection
/// would give us key rotation for free — the IEncryptionService interface is
/// designed for easy swap. For now, key rotation is not implemented.
/// </summary>
public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public AesEncryptionService(IOptions<EncryptionOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.MasterKey);
        if (_key.Length != 32)
            throw new ArgumentException("Encryption master key must be exactly 32 bytes (256 bits) when decoded from base64.");
    }

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Prepend IV to ciphertext: base64(IV + ciphertext)
        var result = new byte[aes.IV.Length + ciphertext.Length];
        aes.IV.CopyTo(result, 0);
        ciphertext.CopyTo(result, aes.IV.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        var combined = Convert.FromBase64String(ciphertext);

        using var aes = Aes.Create();
        aes.Key = _key;

        // Extract IV (first 16 bytes) and ciphertext (remainder)
        var iv = combined[..16];
        var encrypted = combined[16..];

        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plaintext = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
