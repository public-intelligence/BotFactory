using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BotFactory.Core.DTOs;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Services;

public class ElevenLabsVoicesService
{
    private const string CacheKey = "elevenlabs:voices";
    private const string VoicesUrl = "https://api.elevenlabs.io/v2/voices?page_size=100";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private const int MaxPages = 5;

    private readonly HttpClient _http;
    private readonly IAppSettingService _appSettings;
    private readonly IKeyValueStore _kv;
    private readonly ILogger<ElevenLabsVoicesService> _logger;

    public ElevenLabsVoicesService(
        HttpClient http,
        IAppSettingService appSettings,
        IKeyValueStore kv,
        ILogger<ElevenLabsVoicesService> logger)
    {
        _http = http;
        _appSettings = appSettings;
        _kv = kv;
        _logger = logger;
    }

    public async Task<ElevenLabsVoicesResponse> GetVoicesAsync(bool forceRefresh, CancellationToken ct = default)
    {
        var apiKey = await _appSettings.GetDecryptedKeyAsync(ServiceType.Tts);
        if (string.IsNullOrEmpty(apiKey))
            return new ElevenLabsVoicesResponse(false, Array.Empty<VoiceDto>(), null);

        if (!forceRefresh)
        {
            var cached = await _kv.GetAsync<CachedPayload>(CacheKey, ct);
            if (cached is not null)
                return new ElevenLabsVoicesResponse(true, cached.Voices, cached.FetchedAt);
        }

        var voices = await FetchAllAsync(apiKey, ct);
        var fetchedAt = DateTime.UtcNow;
        await _kv.SetAsync(CacheKey, new CachedPayload(voices, fetchedAt), CacheTtl, ct);
        return new ElevenLabsVoicesResponse(true, voices, fetchedAt);
    }

    private async Task<List<VoiceDto>> FetchAllAsync(string apiKey, CancellationToken ct)
    {
        var all = new List<VoiceDto>();
        string? nextToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = VoicesUrl + (nextToken is null ? string.Empty : $"&next_page_token={Uri.EscapeDataString(nextToken)}");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("xi-api-key", apiKey);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("ElevenLabs /v2/voices failed: {Status} {Body}", (int)resp.StatusCode, body);
                resp.EnsureSuccessStatusCode();
            }

            var payload = await resp.Content.ReadFromJsonAsync<ElevenLabsListResponse>(cancellationToken: ct);
            if (payload?.Voices is null) break;

            foreach (var v in payload.Voices)
            {
                if (string.IsNullOrEmpty(v.VoiceId) || string.IsNullOrEmpty(v.Name)) continue;
                all.Add(new VoiceDto(v.VoiceId, v.Name, v.Description, v.PreviewUrl, v.Category));
            }

            if (!payload.HasMore || string.IsNullOrEmpty(payload.NextPageToken)) break;
            nextToken = payload.NextPageToken;
        }

        return all;
    }

    private record CachedPayload(List<VoiceDto> Voices, DateTime FetchedAt);

    private record ElevenLabsListResponse(
        [property: JsonPropertyName("voices")] List<ElevenLabsVoice>? Voices,
        [property: JsonPropertyName("has_more")] bool HasMore,
        [property: JsonPropertyName("next_page_token")] string? NextPageToken);

    private record ElevenLabsVoice(
        [property: JsonPropertyName("voice_id")] string VoiceId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("preview_url")] string? PreviewUrl,
        [property: JsonPropertyName("category")] string? Category);
}
