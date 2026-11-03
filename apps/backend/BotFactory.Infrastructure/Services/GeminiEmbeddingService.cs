using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Services;

public class GeminiEmbeddingService
{
    private readonly HttpClient _http;
    private readonly IAppSettingService _appSettings;
    private readonly ILogger<GeminiEmbeddingService> _logger;

    private const string Model = "gemini-embedding-001";
    private const int BatchSize = 50;
    private const int EmbeddingDimensions = 1536;

    public GeminiEmbeddingService(HttpClient http, IAppSettingService appSettings,
        ILogger<GeminiEmbeddingService> logger)
    {
        _http = http;
        _appSettings = appSettings;
        _logger = logger;
    }

    public virtual async Task<float[]> GenerateQueryEmbeddingAsync(string query, CancellationToken ct = default)
    {
        var results = await GenerateEmbeddingsAsync([query], ct, "RETRIEVAL_QUERY");
        return results[0];
    }

    public virtual async Task<float[][]> GenerateEmbeddingsAsync(List<string> texts, CancellationToken ct = default,
        string taskType = "RETRIEVAL_DOCUMENT")
    {
        var apiKey = await _appSettings.GetDecryptedKeyAsync(ServiceType.Embeddings)
            ?? throw new InvalidOperationException("Embeddings API key not configured. Add a Gemini key in Settings.");

        var results = new float[texts.Count][];

        for (int i = 0; i < texts.Count; i += BatchSize)
        {
            var batch = texts.Skip(i).Take(BatchSize).ToList();
            var batchResults = await CallBatchEmbedAsync(apiKey, batch, taskType, ct);

            for (int j = 0; j < batchResults.Length; j++)
                results[i + j] = batchResults[j];

            _logger.LogDebug("Embedded batch {Start}-{End} of {Total}",
                i, Math.Min(i + BatchSize, texts.Count), texts.Count);
        }

        return results;
    }

    private async Task<float[][]> CallBatchEmbedAsync(string apiKey, List<string> texts, string taskType, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:batchEmbedContents?key={apiKey}";

        var payload = new
        {
            requests = texts.Select(t => new
            {
                model = $"models/{Model}",
                content = new { parts = new[] { new { text = t } } },
                taskType,
                outputDimensionality = EmbeddingDimensions
            }).ToArray()
        };

        var response = await _http.PostAsJsonAsync(url, payload, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BatchEmbedResponse>(ct);
        return result!.Embeddings.Select(e => e.Values).ToArray();
    }

    private record BatchEmbedResponse(
        [property: JsonPropertyName("embeddings")] List<EmbeddingValue> Embeddings);

    private record EmbeddingValue(
        [property: JsonPropertyName("values")] float[] Values);
}
