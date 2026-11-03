using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Services;

public class AppSettingService : IAppSettingService
{
    private readonly BotFactoryDbContext _db;
    private readonly IEncryptionService _encryption;

    private static readonly Dictionary<ServiceType, ProviderType> ServiceTypeProviderMap = new()
    {
        { ServiceType.Llm, ProviderType.Gemini },
        { ServiceType.Stt, ProviderType.ElevenLabs },
        { ServiceType.Tts, ProviderType.ElevenLabs },
        { ServiceType.Embeddings, ProviderType.Gemini },
    };

    public AppSettingService(BotFactoryDbContext db, IEncryptionService encryption)
    {
        _db = db;
        _encryption = encryption;
    }

    public async Task<List<AppSetting>> GetAllAsync()
    {
        return await _db.AppSettings.OrderBy(a => a.ServiceType).ToListAsync();
    }

    public async Task<AppSetting?> GetByServiceTypeAsync(ServiceType serviceType)
    {
        return await _db.AppSettings.FirstOrDefaultAsync(a => a.ServiceType == serviceType);
    }

    public async Task<List<AppSetting>> BulkUpsertAsync(List<(ServiceType ServiceType, string ApiKey)> settings)
    {
        var results = new List<AppSetting>();

        foreach (var (serviceType, apiKey) in settings)
        {
            if (!ServiceTypeProviderMap.TryGetValue(serviceType, out var provider))
                throw new ArgumentException($"Invalid service type: {serviceType}");

            var existing = await _db.AppSettings.FirstOrDefaultAsync(a => a.ServiceType == serviceType);

            if (existing is not null)
            {
                existing.Provider = provider;
                existing.EncryptedApiKey = _encryption.Encrypt(apiKey);
                existing.KeyPrefix = apiKey.Length >= 5 ? apiKey[..5] : apiKey;
                results.Add(existing);
            }
            else
            {
                var setting = new AppSetting
                {
                    ServiceType = serviceType,
                    Provider = provider,
                    EncryptedApiKey = _encryption.Encrypt(apiKey),
                    KeyPrefix = apiKey.Length >= 5 ? apiKey[..5] : apiKey,
                };
                _db.AppSettings.Add(setting);
                results.Add(setting);
            }
        }

        await _db.SaveChangesAsync();
        return results;
    }

    public async Task<bool> DeleteAsync(ServiceType serviceType)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(a => a.ServiceType == serviceType);
        if (setting is null) return false;

        _db.AppSettings.Remove(setting);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<string?> GetDecryptedKeyAsync(ServiceType serviceType)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(a => a.ServiceType == serviceType);
        if (setting is null || string.IsNullOrEmpty(setting.EncryptedApiKey))
            return null;

        return _encryption.Decrypt(setting.EncryptedApiKey);
    }
}
