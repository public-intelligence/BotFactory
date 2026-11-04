using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotFactory.Infrastructure.Services;

public abstract record GeminiStreamEvent;

public sealed record GeminiTextDelta(string Text) : GeminiStreamEvent;

public sealed record GeminiFunctionCall(string Name, JsonElement Args) : GeminiStreamEvent;

/// <summary>
/// Token counts from Gemini's <c>usageMetadata</c> for one generateContent call.
/// Gemini omits a count when it is zero (e.g. no <c>cachedContentTokenCount</c> on a
/// cache miss, no <c>thoughtsTokenCount</c> with thinking off), so a missing field
/// reads as 0. <see cref="PromptTokens"/> already includes <see cref="CachedTokens"/>.
/// <see cref="TotalTokens"/> is what Gemini reports (prompt + output + thoughts).
/// </summary>
public sealed record GeminiUsage(
    int PromptTokens,
    int OutputTokens,
    int CachedTokens,
    int ThoughtsTokens,
    int TotalTokens) : GeminiStreamEvent
{
    /// <summary>Share of the prompt served from Gemini's cache, 0-100.</summary>
    public int CachedPercent => PromptTokens > 0 ? (int)Math.Round(100.0 * CachedTokens / PromptTokens) : 0;

    /// <summary>One-line form for the telemetry summary, e.g. <c>in=12000 cached=11500 (96%) out=40 thoughts=0 total=12040</c>.</summary>
    public string Describe() =>
        $"in={PromptTokens} cached={CachedTokens} ({CachedPercent}%) out={OutputTokens} thoughts={ThoughtsTokens} total={TotalTokens}";
}

/// <summary>
/// Why Gemini stopped generating (<c>finishReason</c>): <c>STOP</c> for a normal end,
/// <c>MAX_TOKENS</c> when the answer hit the output limit and was cut.
/// </summary>
public sealed record GeminiFinish(string Reason) : GeminiStreamEvent;

/// <summary>
/// Extra Gemini API <c>contents</c> turns (e.g. model functionCall + user functionResponse) within a single voice turn.
/// </summary>
public sealed record GeminiExtraContent(string Role, object[] Parts);

public class GeminiChatService
{
    private const string GenerateContentBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    // camelCase keys + drop nulls so the typed FunctionDeclaration / JsonSchema
    // records serialize to the exact wire shape Gemini expects.
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly IAppSettingService _appSettings;
    private readonly IOptions<GeminiChatOptions> _options;
    private readonly ILogger<GeminiChatService> _logger;
    private readonly IToolRegistry _tools;

    public GeminiChatService(
        HttpClient http,
        IAppSettingService appSettings,
        IOptions<GeminiChatOptions> options,
        ILogger<GeminiChatService> logger,
        IToolRegistry tools)
    {
        _http = http;
        _appSettings = appSettings;
        _options = options;
        _logger = logger;
        _tools = tools;
    }

    /// <summary>
    /// Streams assistant output from Gemini (SSE): text deltas and/or function calls.
    /// </summary>
    /// <param name="toolChoice">Tool-use policy for this call; AUTO unless the turn needs
    /// a text-only answer (NONE) or a mandatory tool call (ANY).</param>
    public async IAsyncEnumerable<GeminiStreamEvent> StreamAsync(
        string systemInstruction,
        IReadOnlyList<GeminiChatMessage> history,
        IReadOnlyList<GeminiExtraContent> extraContents,
        string? newUserText,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        ToolChoice toolChoice = ToolChoice.Auto,
        double? temperature = null)
    {
        var apiKey = await _appSettings.GetDecryptedKeyAsync(ServiceType.Llm)
            ?? throw new InvalidOperationException(
                "LLM API key not configured. Add a Gemini key for the LLM stage in Settings.");

        var model = _options.Value.ChatModel;
        var url =
            $"{GenerateContentBaseUrl}/{model}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(apiKey)}";

        var payload = BuildStreamRequest(
            systemInstruction,
            history,
            extraContents,
            newUserText,
            toolChoice,
            temperature);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: WireOptions),
        };

        // HttpClient.Timeout stops guarding once the headers arrive (we read the body as a
        // stream), so a stream that stalls halfway would hang the turn. This limit covers
        // the whole call; when it fires the caller gets a TimeoutException it can retry.
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.Value.HttpTimeoutSeconds));
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callCts.CancelAfter(timeout);
        var callToken = callCts.Token;

        // Until Gemini sends its first chunk a shorter limit applies: a call that sits on the
        // request for 30 s is better cut and retried than waited for (see FirstByteTimeoutSeconds).
        // After the first chunk only the limit of the whole call is left.
        var firstByteLimit = TimeSpan.FromSeconds(Math.Max(0, _options.Value.FirstByteTimeoutSeconds));
        var limitFirstByte = firstByteLimit > TimeSpan.Zero && firstByteLimit < timeout;
        using var firstByteCts = CancellationTokenSource.CreateLinkedTokenSource(callToken);
        if (limitFirstByte)
            firstByteCts.CancelAfter(firstByteLimit);
        var waitToken = firstByteCts.Token;
        var waitLimit = limitFirstByte ? firstByteLimit : timeout;

        using var response = await WithinTimeoutAsync(
            () => _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, waitToken),
            waitLimit,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await WithinTimeoutAsync(
                () => response.Content.ReadAsStringAsync(callToken),
                timeout,
                cancellationToken);
            var status = (int)response.StatusCode;
            _logger.LogError(
                "Gemini streamGenerateContent failed status={Status} body={Body}",
                status,
                errBody.Length > 2000 ? errBody[..2000] : errBody);
            var info = ProviderErrorClassifier.ClassifyGemini(status, errBody);
            var retryAfter = ProviderErrorClassifier.ParseRetryDelay(errBody) ?? RetryAfterHeader(response);
            throw new ProviderServiceException(info, errBody, status, retryAfter);
        }

        await using var stream = await WithinTimeoutAsync(
            () => response.Content.ReadAsStreamAsync(waitToken),
            waitLimit,
            cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var firstChunkSeen = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readToken = firstChunkSeen ? callToken : waitToken;
            var readLimit = firstChunkSeen ? timeout : waitLimit;
            var line = await WithinTimeoutAsync(
                () => reader.ReadLineAsync(readToken).AsTask(),
                readLimit,
                cancellationToken);
            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':'))
                continue;

            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line.AsSpan(5).Trim();
            if (data.Length == 0 || data.SequenceEqual("[DONE]"))
                continue;

            IEnumerable<GeminiStreamEvent> events;
            try
            {
                events = ParseStreamDataEvents(data.ToString());
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping malformed Gemini SSE JSON");
                continue;
            }

            // Gemini has started answering: from here on only the whole-call limit applies, so the
            // first-byte timer is disarmed (the token it belongs to was also given to SendAsync).
            if (!firstChunkSeen)
            {
                firstChunkSeen = true;
                firstByteCts.CancelAfter(Timeout.InfiniteTimeSpan);
            }

            foreach (var ev in events)
                yield return ev;
        }
    }

    /// <summary>
    /// Runs one step of the call. A cancellation that did not come from the caller is
    /// the call's own time limit (or HttpClient's), surfaced as a TimeoutException so
    /// the turn can tell it apart from a barge-in.
    /// </summary>
    /// <summary>The wait a <c>Retry-After</c> header asks for, in seconds or as a date.</summary>
    private static TimeSpan? RetryAfterHeader(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
            return null;
        if (header.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    private static async Task<T> WithinTimeoutAsync<T>(
        Func<Task<T>> step,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        try
        {
            return await step();
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Gemini did not answer within {(int)timeout.TotalSeconds}s.", ex);
        }
    }

    private object BuildStreamRequest(
        string systemInstruction,
        IReadOnlyList<GeminiChatMessage> history,
        IReadOnlyList<GeminiExtraContent> extraContents,
        string? newUserText,
        ToolChoice toolChoice,
        double? temperature)
    {
        var contents = new List<object>();
        foreach (var m in history)
        {
            // The lookups behind a past answer go right before it, as Gemini expects:
            // model functionCall(s), then user functionResponse(s), then the answer.
            if (m.Role != MessageRole.User && m.ToolExchanges is { Count: > 0 } exchanges)
            {
                contents.Add(new
                {
                    role = "model",
                    parts = exchanges
                        .Select(e => (object)new { functionCall = new { name = e.Name, args = e.Args } })
                        .ToArray(),
                });
                contents.Add(new
                {
                    role = "user",
                    parts = exchanges
                        .Select(e => (object)new
                        {
                            functionResponse = new
                            {
                                name = e.Name,
                                response = new { solicitud = e.Args, resultado = ResultForModel(e.Result) },
                            },
                        })
                        .ToArray(),
                });
            }

            contents.Add(new
            {
                role = m.Role == MessageRole.User ? "user" : "model",
                parts = new[] { new { text = m.Content } },
            });
        }

        if (!string.IsNullOrEmpty(newUserText))
        {
            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = newUserText } },
            });
        }

        foreach (var ex in extraContents)
        {
            contents.Add(new
            {
                role = ex.Role,
                parts = ex.Parts,
            });
        }

        var body = new Dictionary<string, object?>
        {
            ["systemInstruction"] = new { parts = new[] { new { text = systemInstruction } } },
            ["contents"] = contents.ToArray(),
            // Bound answer length (voice replies must stay short), lower the
            // default ~1.0 temperature to reduce topic drift, and disable
            // gemini-2.5-flash "thinking" by default for lower voice latency.
            ["generationConfig"] = new
            {
                temperature = temperature ?? _options.Value.Temperature,
                maxOutputTokens = _options.Value.MaxOutputTokens,
                thinkingConfig = new { thinkingBudget = _options.Value.ThinkingBudget },
            },
        };

        var toolsPayload = _tools.BuildGeminiToolsPayload();
        if (toolsPayload.Length > 0)
        {
            body["tools"] = toolsPayload;
            var mode = toolChoice switch
            {
                ToolChoice.None => "NONE",
                ToolChoice.Required => "ANY",
                _ => "AUTO",
            };
            body["toolConfig"] = new { functionCallingConfig = new { mode } };
        }

        return body;
    }

    /// <summary>A stored result as JSON when it parses; a trimmed excerpt goes as text.</summary>
    private static object ResultForModel(string result)
    {
        try
        {
            using var doc = JsonDocument.Parse(result);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new { recortado = result };
        }
    }

    private static IEnumerable<GeminiStreamEvent> ParseStreamDataEvents(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var ev in ParseStreamChunkEvents(doc.RootElement))
            yield return ev;
    }

    public static IEnumerable<GeminiStreamEvent> ParseStreamChunkEvents(JsonElement root)
    {
        // Usage first, before any part: the turn stops reading the stream as soon as
        // it sees a function call, so a usage event placed after the parts of that
        // chunk would never be delivered. Every chunk repeats usageMetadata with
        // running counts; the consumer keeps the last one it saw.
        if (root.TryGetProperty("usageMetadata", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
        {
            yield return new GeminiUsage(
                ReadCount(usageEl, "promptTokenCount"),
                ReadCount(usageEl, "candidatesTokenCount"),
                ReadCount(usageEl, "cachedContentTokenCount"),
                ReadCount(usageEl, "thoughtsTokenCount"),
                ReadCount(usageEl, "totalTokenCount"));
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            yield break;

        var candidate = candidates[0];
        if (candidate.TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts))
        {
            foreach (var ev in ParseParts(parts))
                yield return ev;
        }

        // After the parts: the reason belongs to the chunk that closes the answer.
        if (candidate.TryGetProperty("finishReason", out var finishEl)
            && finishEl.ValueKind == JsonValueKind.String
            && finishEl.GetString() is { Length: > 0 } reason)
        {
            yield return new GeminiFinish(reason);
        }
    }

    private static IEnumerable<GeminiStreamEvent> ParseParts(JsonElement parts)
    {
        if (parts.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("functionCall", out var fc))
            {
                var name = fc.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                JsonElement args;
                if (fc.TryGetProperty("args", out var argsEl))
                    args = argsEl.Clone();
                else
                {
                    using var emptyArgs = JsonDocument.Parse("{}");
                    args = emptyArgs.RootElement.Clone();
                }

                yield return new GeminiFunctionCall(name, args);
            }

            if (part.TryGetProperty("text", out var textEl))
            {
                var t = textEl.GetString();
                if (!string.IsNullOrEmpty(t))
                    yield return new GeminiTextDelta(t);
            }
        }
    }

    private static int ReadCount(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)
            ? Math.Max(0, n)
            : 0;
}
