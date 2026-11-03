using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

public interface IAppSettingService
{
    Task<List<AppSetting>> GetAllAsync();
    Task<AppSetting?> GetByServiceTypeAsync(ServiceType serviceType);
    Task<List<AppSetting>> BulkUpsertAsync(List<(ServiceType ServiceType, string ApiKey)> settings);
    Task<bool> DeleteAsync(ServiceType serviceType);
    Task<string?> GetDecryptedKeyAsync(ServiceType serviceType);
}
