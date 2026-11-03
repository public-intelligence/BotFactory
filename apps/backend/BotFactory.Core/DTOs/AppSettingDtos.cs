namespace BotFactory.Core.DTOs;

public record AppSettingResponse(
    int Id,
    string ServiceType,
    string Provider,
    string KeyPrefix,
    bool HasKey,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record UpsertAppSettingRequest(
    string ServiceType,
    string ApiKey);

public record BulkUpsertAppSettingsRequest(
    List<UpsertAppSettingRequest> Settings);
