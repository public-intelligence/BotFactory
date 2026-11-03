using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BotFactory.Core.Entities;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Tools.Execution;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Tools.Connectors;

/// <summary>
/// REST/JSON protocol adapter. Substitutes validated argument values into the
/// definition's fixed templates (path, query, body) — the model never builds a
/// request — pins the resolved URL to the connection's BaseUrl host, applies
/// the egress guard, per-call timeout, response size cap, and one retry for
/// idempotent GETs on transient failures. Never throws for external failures:
/// every problem maps to a ToolConnectorResult the gateway records verbatim.
/// </summary>
public partial class RestToolConnector : IToolConnector
{
    public const string HttpClientName = "tool-egress";

    private const int ErrorSnippetMaxChars = 2000;

    [GeneratedRegex("\\{([a-zA-Z0-9_]+)\\}")]
    private static partial Regex PlaceholderRegex();

    private readonly IHttpClientFactory _httpFactory;
    private readonly IEgressGuard _egress;
    private readonly ILogger<RestToolConnector> _logger;

    public RestToolConnector(
        IHttpClientFactory httpFactory,
        IEgressGuard egress,
        ILogger<RestToolConnector> logger)
    {
        _httpFactory = httpFactory;
        _egress = egress;
        _logger = logger;
    }

    public ToolConnectorType Type => ToolConnectorType.Rest;

    public async Task<ToolConnectorResult> InvokeAsync(
        ToolConnectorRequest request,
        CancellationToken cancellationToken)
    {
        var definition = request.Definition;
        var connection = request.Connection;
        var argValues = ExtractArgValues(request.Args);
        if (definition.NormalizeArgDiacritics)
        {
            NormalizeStringArgs(argValues);
        }

        // ── Resolve URL ────────────────────────────────────────────────────
        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return Fail(ToolExecutionOutcome.ConnectorError, "invalid_base_url",
                "La URL base de la conexión no es válida.", $"{definition.HttpMethod} <sin URL>");
        }

        string resolvedPath;
        try
        {
            resolvedPath = ResolveTemplate(
                definition.PathTemplate.StartsWith('/') ? definition.PathTemplate : "/" + definition.PathTemplate,
                argValues,
                Uri.EscapeDataString,
                requireAll: true);
        }
        catch (TemplateParamMissingException ex)
        {
            return Fail(ToolExecutionOutcome.ConnectorError, "template_param_missing",
                $"Falta el parámetro '{ex.ParamName}' requerido por la plantilla de ruta.",
                $"{definition.HttpMethod} {connection.BaseUrl}{definition.PathTemplate}");
        }

        var query = BuildQueryString(definition.QueryParamsJson, argValues);
        var fullUrl = connection.BaseUrl.TrimEnd('/') + resolvedPath + query;

        if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != baseUri.Scheme
            || !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != baseUri.Port)
        {
            // Placeholders are escaped, so this only trips on hostile config —
            // but the pin is absolute: nothing leaves the connection's host.
            return Fail(ToolExecutionOutcome.ConnectorError, "url_escape",
                "La URL resuelta se sale del host configurado en la conexión.",
                $"{definition.HttpMethod} {ToolRedaction.Truncate(fullUrl, 500)}");
        }

        var requestSummary = $"{definition.HttpMethod} {RedactQueryValues(uri)}";

        var (egressOk, egressError) = await _egress.ValidateAsync(uri, cancellationToken);
        if (!egressOk)
        {
            return Fail(ToolExecutionOutcome.ConnectorError, "egress_blocked",
                egressError ?? "Destino no permitido.", requestSummary);
        }

        // ── Execute (per-call timeout; one retry for idempotent GETs) ──────
        var timeoutMs = definition.TimeoutMsOverride ?? connection.TimeoutMs;
        var isGet = HttpMethod.Parse(definition.HttpMethod) == HttpMethod.Get;
        var maxAttempts = isGet ? 2 : 1;
        var http = _httpFactory.CreateClient(HttpClientName);

        HttpResponseMessage? response = null;
        var attempt = 0;

        while (true)
        {
            attempt++;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(1, timeoutMs));

            HttpRequestMessage httpRequest;
            try
            {
                httpRequest = BuildHttpRequest(definition, connection, request.Credentials, uri, argValues);
            }
            catch (CredentialsMissingException)
            {
                return Fail(ToolExecutionOutcome.ConnectorError, "credentials_missing",
                    "La conexión no tiene configuradas las credenciales requeridas.", requestSummary);
            }

            try
            {
                using (httpRequest)
                {
                    response = await http.SendAsync(
                        httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                }

                var status = (int)response.StatusCode;
                var retriableStatus = status is 502 or 503 or 504;
                if (retriableStatus && attempt < maxAttempts)
                {
                    response.Dispose();
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // turn cancelled (barge-in / user cancel) — gateway records Cancelled
            }
            catch (OperationCanceledException)
            {
                return Fail(ToolExecutionOutcome.Timeout, "timeout",
                    $"El servicio externo no respondió en {timeoutMs} ms.", requestSummary,
                    retryCount: attempt - 1);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < maxAttempts)
                {
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                _logger.LogWarning(ex, "Tool egress network failure for {Url}", requestSummary);
                return Fail(ToolExecutionOutcome.ConnectorError, "network",
                    "No se pudo conectar con el servicio externo.", requestSummary,
                    retryCount: attempt - 1);
            }
        }

        using (response)
        {
            var statusCode = (int)response.StatusCode;
            var retryCount = attempt - 1;

            if (statusCode is >= 200 and < 300)
            {
                var (payload, bytes, readError) = await ReadJsonBodyAsync(
                    response, connection.MaxResponseBytes, cancellationToken);

                if (readError is not null)
                {
                    return Fail(
                        readError == "response_too_large"
                            ? ToolExecutionOutcome.ConnectorError
                            : ToolExecutionOutcome.ConnectorError,
                        readError,
                        readError == "response_too_large"
                            ? "La respuesta del servicio externo excede el tamaño permitido."
                            : "La respuesta del servicio externo no es JSON válido.",
                        requestSummary, statusCode, bytes, retryCount);
                }

                var mapped = ApplyResponseMapping(definition.ResponseMappingJson, payload!.Value);

                return new ToolConnectorResult(
                    ToolExecutionOutcome.Success,
                    statusCode,
                    mapped,
                    bytes,
                    retryCount,
                    requestSummary);
            }

            // Non-2xx (redirects included — auto-redirect is off).
            var errorBody = await ReadErrorSnippetAsync(response, cancellationToken);
            var userFacing = MapErrorMessage(definition.ErrorMappingJson, statusCode);

            return new ToolConnectorResult(
                ToolExecutionOutcome.HttpError,
                statusCode,
                Payload: null,
                ResponseBytes: errorBody.Length,
                RetryCount: retryCount,
                ResolvedRequestSummary: requestSummary,
                ErrorCategory: $"http_{statusCode}",
                ErrorMessage: ToolRedaction.Truncate(errorBody, ErrorSnippetMaxChars),
                UserFacingError: userFacing);
        }

        static ToolConnectorResult Fail(
            ToolExecutionOutcome outcome, string category, string message, string summary,
            int? httpStatus = null, long bytes = 0, int retryCount = 0) =>
            new(outcome, httpStatus, null, bytes, retryCount, summary,
                ErrorCategory: category, ErrorMessage: message, UserFacingError: message);
    }

    // ── Template + request building ────────────────────────────────────────

    private sealed class TemplateParamMissingException(string paramName) : Exception
    {
        public string ParamName { get; } = paramName;
    }

    private sealed class CredentialsMissingException : Exception;

    private static Dictionary<string, JsonElement> ExtractArgValues(JsonElement args)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (args.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in args.EnumerateObject())
                values[prop.Name] = prop.Value;
        }

        return values;
    }

    private static string ArgToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),
    };

    private static void NormalizeStringArgs(Dictionary<string, JsonElement> argValues)
    {
        foreach (var key in argValues.Keys.ToList())
        {
            var element = argValues[key];
            if (element.ValueKind != JsonValueKind.String) continue;

            var raw = element.GetString();
            if (raw is null) continue;

            var stripped = DiacriticsNormalizer.Strip(raw);
            if (!ReferenceEquals(stripped, raw))
            {
                argValues[key] = JsonSerializer.SerializeToElement(stripped);
            }
        }
    }

    private static string ResolveTemplate(
        string template,
        IReadOnlyDictionary<string, JsonElement> args,
        Func<string, string> encode,
        bool requireAll)
    {
        return PlaceholderRegex().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!args.TryGetValue(name, out var value))
            {
                if (requireAll)
                    throw new TemplateParamMissingException(name);
                return string.Empty;
            }

            return encode(ArgToString(value));
        });
    }

    private static string BuildQueryString(
        string? queryParamsJson,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (string.IsNullOrWhiteSpace(queryParamsJson))
            return string.Empty;

        Dictionary<string, string>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<Dictionary<string, string>>(queryParamsJson);
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        if (entries is null || entries.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var (name, template) in entries)
        {
            // A pure "{param}" reference whose argument is absent is simply
            // omitted (optional query parameter).
            var pure = PlaceholderRegex().Match(template);
            if (pure.Success && pure.Value == template
                && !args.ContainsKey(pure.Groups[1].Value))
            {
                continue;
            }

            var value = ResolveTemplate(template, args, s => s, requireAll: false);
            parts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private HttpRequestMessage BuildHttpRequest(
        ToolDefinition definition,
        ToolConnection connection,
        ToolCredentials credentials,
        Uri uri,
        IReadOnlyDictionary<string, JsonElement> argValues)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Parse(definition.HttpMethod), uri);

        AddHeaders(httpRequest, connection.DefaultHeadersJson);
        AddHeaders(httpRequest, definition.StaticHeadersJson);
        ApplyAuth(httpRequest, connection, credentials);

        if (!string.IsNullOrWhiteSpace(definition.BodyTemplateJson)
            && httpRequest.Method != HttpMethod.Get)
        {
            var body = ResolveBodyTemplate(definition.BodyTemplateJson, argValues);
            httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return httpRequest;
    }

    private static void AddHeaders(HttpRequestMessage request, string? headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
            return;

        Dictionary<string, string>? headers;
        try
        {
            headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
        }
        catch (JsonException)
        {
            return;
        }

        if (headers is null)
            return;

        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
    }

    private void ApplyAuth(
        HttpRequestMessage request,
        ToolConnection connection,
        ToolCredentials credentials)
    {
        switch (connection.AuthType)
        {
            case ToolAuthType.None:
                return;

            case ToolAuthType.ApiKeyHeader:
            {
                var headerName = ReadAuthConfig(connection, "headerName") ?? "X-Api-Key";
                var apiKey = credentials.Get("apiKey") ?? throw new CredentialsMissingException();
                request.Headers.TryAddWithoutValidation(headerName, apiKey);
                return;
            }

            case ToolAuthType.Bearer:
            {
                var token = credentials.Get("apiKey") ?? throw new CredentialsMissingException();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return;
            }

            case ToolAuthType.Basic:
            {
                var username = ReadAuthConfig(connection, "username") ?? string.Empty;
                var password = credentials.Get("password") ?? throw new CredentialsMissingException();
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                return;
            }

            case ToolAuthType.ApiKeyQuery:
            {
                // Injected here — after the request summary was computed from the
                // credential-free URI — so the key stays out of records/logs even
                // when the param name (e.g. "access_key") wouldn't match the
                // redaction list.
                var paramName = ReadAuthConfig(connection, "paramName") ?? "api_key";
                var apiKey = credentials.Get("apiKey") ?? throw new CredentialsMissingException();
                request.RequestUri = AppendQueryParam(request.RequestUri!, paramName, apiKey);
                return;
            }
        }
    }

    private static Uri AppendQueryParam(Uri uri, string name, string value)
    {
        var pair = $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri(uri.AbsoluteUri + separator + pair);
    }

    private static string? ReadAuthConfig(ToolConnection connection, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(connection.AuthConfigJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(key, out var el)
                && el.ValueKind == JsonValueKind.String
                    ? el.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResolveBodyTemplate(
        string bodyTemplateJson,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        JsonNode? template;
        try
        {
            template = JsonNode.Parse(bodyTemplateJson);
        }
        catch (JsonException)
        {
            return "{}";
        }

        var resolved = ResolveBodyNode(template, args);
        return resolved?.ToJsonString() ?? "{}";
    }

    private static JsonNode? ResolveBodyNode(
        JsonNode? node,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                    result[key] = ResolveBodyNode(value, args);
                return result;
            }

            case JsonArray arr:
            {
                var result = new JsonArray();
                foreach (var item in arr)
                    result.Add(ResolveBodyNode(item, args));
                return result;
            }

            case JsonValue value when value.TryGetValue<string>(out var s):
            {
                // A value that is exactly "{param}" keeps the argument's JSON
                // type (numbers stay numbers); embedded placeholders interpolate.
                var pure = PlaceholderRegex().Match(s);
                if (pure.Success && pure.Value == s)
                {
                    return args.TryGetValue(pure.Groups[1].Value, out var arg)
                        ? JsonNode.Parse(arg.GetRawText())
                        : null;
                }

                return JsonValue.Create(
                    ResolveTemplate(s, args, v => v, requireAll: false));
            }

            default:
                return node?.DeepClone();
        }
    }

    // ── Response handling ──────────────────────────────────────────────────

    private static async Task<(JsonElement? Payload, long Bytes, string? Error)> ReadJsonBodyAsync(
        HttpResponseMessage response,
        int maxResponseBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();

        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;

            if (buffer.Length + read > maxResponseBytes)
                return (null, buffer.Length + read, "response_too_large");

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            using var empty = JsonDocument.Parse("{}");
            return (empty.RootElement.Clone(), 0, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
            return (doc.RootElement.Clone(), buffer.Length, null);
        }
        catch (JsonException)
        {
            return (null, buffer.Length, "invalid_json");
        }
    }

    private static async Task<string> ReadErrorSnippetAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[ErrorSnippetMaxChars];
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static JsonElement ApplyResponseMapping(string? responseMappingJson, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(responseMappingJson))
            return payload;

        List<ResponseMappingEntry>? picks;
        try
        {
            picks = JsonSerializer.Deserialize<List<ResponseMappingEntry>>(
                responseMappingJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return payload;
        }

        if (picks is null || picks.Count == 0)
            return payload;

        var result = new JsonObject();
        foreach (var pick in picks)
        {
            if (string.IsNullOrWhiteSpace(pick.Path))
                continue;

            var value = NavigateDotPath(payload, pick.Path);
            var name = string.IsNullOrWhiteSpace(pick.Name) ? pick.Path : pick.Name;
            result[name] = value is { } v ? JsonNode.Parse(v.GetRawText()) : null;
        }

        using var doc = JsonDocument.Parse(result.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static JsonElement? NavigateDotPath(JsonElement element, string dotPath)
    {
        var current = element;
        foreach (var segment in dotPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind == JsonValueKind.Object
                && current.TryGetProperty(segment, out var next))
            {
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array
                && int.TryParse(segment, out var index)
                && index >= 0 && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static string? MapErrorMessage(string? errorMappingJson, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(errorMappingJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(errorMappingJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (doc.RootElement.TryGetProperty(
                    statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture), out var exact)
                && exact.ValueKind == JsonValueKind.String)
            {
                return exact.GetString();
            }

            return doc.RootElement.TryGetProperty("default", out var fallback)
                && fallback.ValueKind == JsonValueKind.String
                    ? fallback.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RedactQueryValues(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query))
            return uri.GetLeftPart(UriPartial.Path);

        var redacted = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                    return pair;

                var name = pair[..eq];
                return ToolRedaction.IsSensitiveKey(Uri.UnescapeDataString(name))
                    ? $"{name}={ToolRedaction.Mask}"
                    : pair;
            });

        return uri.GetLeftPart(UriPartial.Path) + "?" + string.Join('&', redacted);
    }

    private sealed record ResponseMappingEntry(string? Path, string? Name);
}
