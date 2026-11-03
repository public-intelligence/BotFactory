namespace BotFactory.Core.Entities;

public class AppSetting : BaseEntity, IAuditable
{
    public ServiceType ServiceType { get; set; }
    public ProviderType Provider { get; set; }

    /// <summary>
    /// AES-encrypted API key. Never returned in plaintext to the client.
    /// </summary>
    [AuditMask]
    public string EncryptedApiKey { get; set; } = string.Empty;

    /// <summary>
    /// First 5 characters of the original key, stored at write time for masked display.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;
}
