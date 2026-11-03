using System.Net;
using System.Text;
using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Tools.Connectors;
using Microsoft.Extensions.Logging.Abstractions;

namespace BotFactory.Tests;

public class RestToolConnectorTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> RequestBodies { get; } = [];
        public Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
            (_, _, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"ok":true}"""));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return await Responder(request, Requests.Count, cancellationToken);
        }

        public static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(100) };
    }

    private sealed class AllowAllEgress : IEgressGuard
    {
        public Task<(bool Ok, string? Error)> ValidateAsync(Uri uri, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));
    }

    private sealed class DenyAllEgress : IEgressGuard
    {
        public Task<(bool Ok, string? Error)> ValidateAsync(Uri uri, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((false, "bloqueado"));
    }

    private static RestToolConnector Connector(HttpMessageHandler handler, IEgressGuard? egress = null) =>
        new(new StubHttpFactory(handler), egress ?? new AllowAllEgress(), NullLogger<RestToolConnector>.Instance);

    private static ToolConnection Connection(Action<ToolConnection>? configure = null)
    {
        var connection = new ToolConnection
        {
            Name = "test-conn",
            BaseUrl = "https://api.example.test",
            TimeoutMs = 2000,
            MaxResponseBytes = 4096,
        };
        configure?.Invoke(connection);
        return connection;
    }

    private static ToolDefinition Definition(Action<ToolDefinition>? configure = null)
    {
        var definition = new ToolDefinition
        {
            Name = "consultar_estado",
            DisplayName = "Consultar estado",
            HttpMethod = "GET",
            PathTemplate = "/estado/{numero}",
            ParametersSchemaJson = """{"type":"object","properties":{"numero":{"type":"string"}}}""",
        };
        configure?.Invoke(definition);
        return definition;
    }

    private static JsonElement Args(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static ToolConnectorRequest Request(
        ToolDefinition definition, ToolConnection connection, string argsJson,
        ToolCredentials? credentials = null) =>
        new(definition, connection, Args(argsJson), credentials ?? ToolCredentials.Empty);

    [Fact]
    public async Task PathTemplate_SubstitutesAndEncodesValues()
    {
        var handler = new StubHandler();
        var result = await Connector(handler).InvokeAsync(
            Request(Definition(), Connection(), """{"numero":"EXP 1/2"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.Success, result.Outcome);
        Assert.Equal("https://api.example.test/estado/EXP%201%2F2",
            handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task HostilePathArgument_CannotEscapeConnectionHost()
    {
        var handler = new StubHandler();
        var result = await Connector(handler).InvokeAsync(
            Request(Definition(), Connection(), """{"numero":"../..@evil.test/x"}"""),
            CancellationToken.None);

        // The value is percent-encoded into the path; host stays pinned.
        Assert.Equal(ToolExecutionOutcome.Success, result.Outcome);
        Assert.Equal("api.example.test", handler.Requests.Single().RequestUri!.Host);
    }

    [Fact]
    public async Task QueryParams_OmitOptionalMissingArgs()
    {
        var handler = new StubHandler();
        var definition = Definition(d =>
        {
            d.PathTemplate = "/buscar";
            d.QueryParamsJson = """{"numero":"{numero}","tipo":"{tipo}","fijo":"literal"}""";
        });

        await Connector(handler).InvokeAsync(
            Request(definition, Connection(), """{"numero":"E1"}"""),
            CancellationToken.None);

        var uri = handler.Requests.Single().RequestUri!;
        Assert.Contains("numero=E1", uri.Query, StringComparison.Ordinal);
        Assert.Contains("fijo=literal", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("tipo=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiKeyHeaderAuth_InjectsCredentialHeader()
    {
        var handler = new StubHandler();
        var connection = Connection(c =>
        {
            c.AuthType = ToolAuthType.ApiKeyHeader;
            c.AuthConfigJson = """{"headerName":"X-Registro-Key"}""";
        });

        await Connector(handler).InvokeAsync(
            Request(Definition(), connection, """{"numero":"1"}""",
                new ToolCredentials(new Dictionary<string, string> { ["apiKey"] = "sekret123" })),
            CancellationToken.None);

        Assert.Equal("sekret123",
            handler.Requests.Single().Headers.GetValues("X-Registro-Key").Single());
    }

    [Fact]
    public async Task ApiKeyQueryAuth_InjectsParam_ButKeepsSecretOutOfSummary()
    {
        var handler = new StubHandler();
        var connection = Connection(c =>
        {
            c.AuthType = ToolAuthType.ApiKeyQuery;
            c.AuthConfigJson = """{"paramName":"access_key"}""";
        });
        var definition = Definition(d =>
        {
            d.PathTemplate = "/current";
            d.QueryParamsJson = """{"query":"{lugar}"}""";
            d.ParametersSchemaJson = """{"type":"object","properties":{"lugar":{"type":"string"}}}""";
        });

        var result = await Connector(handler).InvokeAsync(
            Request(definition, connection, """{"lugar":"New Delhi"}""",
                new ToolCredentials(new Dictionary<string, string> { ["apiKey"] = "sekret123" })),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.Success, result.Outcome);

        // The external request carries both the templated query and the key…
        var uri = handler.Requests.Single().RequestUri!;
        Assert.Contains("query=New%20Delhi", uri.Query, StringComparison.Ordinal);
        Assert.Contains("access_key=sekret123", uri.Query, StringComparison.Ordinal);

        // …but the recorded summary was built before injection: no secret, even
        // though "access_key" wouldn't match the sensitive-key redaction list.
        Assert.DoesNotContain("sekret123", result.ResolvedRequestSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("access_key", result.ResolvedRequestSummary, StringComparison.Ordinal);
        Assert.Contains("query=New%20Delhi", result.ResolvedRequestSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiKeyQueryAuth_MissingCredential_FailsWithoutCallingNetwork()
    {
        var handler = new StubHandler();
        var connection = Connection(c => c.AuthType = ToolAuthType.ApiKeyQuery);

        var result = await Connector(handler).InvokeAsync(
            Request(Definition(), connection, """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.ConnectorError, result.Outcome);
        Assert.Equal("credentials_missing", result.ErrorCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MissingCredentials_FailWithoutCallingNetwork()
    {
        var handler = new StubHandler();
        var connection = Connection(c => c.AuthType = ToolAuthType.Bearer);

        var result = await Connector(handler).InvokeAsync(
            Request(Definition(), connection, """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.ConnectorError, result.Outcome);
        Assert.Equal("credentials_missing", result.ErrorCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BodyTemplate_PreservesArgumentTypes()
    {
        var handler = new StubHandler();
        var definition = Definition(d =>
        {
            d.HttpMethod = "POST";
            d.PathTemplate = "/validar";
            d.BodyTemplateJson = """{"numero":"{numero}","anio":"{anio}","canal":"voz-bot"}""";
        });

        await Connector(handler).InvokeAsync(
            Request(definition, Connection(), """{"numero":"E1","anio":2026}"""),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.RequestBodies.Single()!);
        Assert.Equal("E1", body.RootElement.GetProperty("numero").GetString());
        Assert.Equal(2026, body.RootElement.GetProperty("anio").GetInt32());
        Assert.Equal("voz-bot", body.RootElement.GetProperty("canal").GetString());
    }

    [Fact]
    public async Task NormalizeArgDiacritics_On_StripsAccentsInPathAndQuery()
    {
        var handler = new StubHandler();
        var definition = Definition(d =>
        {
            d.NormalizeArgDiacritics = true;
            d.PathTemplate = "/clima/{ciudad}";
            d.QueryParamsJson = """{"query":"{lugar}"}""";
        });

        var result = await Connector(handler).InvokeAsync(
            Request(definition, Connection(), """{"ciudad":"San José","lugar":"Güira Ñuñoa"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.Success, result.Outcome);
        var uri = handler.Requests.Single().RequestUri!;
        Assert.Contains("/clima/San%20Jose", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("query=Guira%20Nunoa", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NormalizeArgDiacritics_Off_PreservesAccents()
    {
        var handler = new StubHandler();
        var definition = Definition(d =>
        {
            d.PathTemplate = "/clima";
            d.QueryParamsJson = """{"query":"{lugar}"}""";
        });

        await Connector(handler).InvokeAsync(
            Request(definition, Connection(), """{"lugar":"San José"}"""),
            CancellationToken.None);

        Assert.Contains("query=San%20Jos%C3%A9",
            handler.Requests.Single().RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NormalizeArgDiacritics_On_BodyTemplate_NormalizesStringsKeepsTypes()
    {
        var handler = new StubHandler();
        var definition = Definition(d =>
        {
            d.NormalizeArgDiacritics = true;
            d.HttpMethod = "POST";
            d.PathTemplate = "/validar";
            d.BodyTemplateJson = """{"lugar":"{lugar}","anio":"{anio}"}""";
        });

        await Connector(handler).InvokeAsync(
            Request(definition, Connection(), """{"lugar":"Niño","anio":2026}"""),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.RequestBodies.Single()!);
        Assert.Equal("Nino", body.RootElement.GetProperty("lugar").GetString());
        Assert.Equal(2026, body.RootElement.GetProperty("anio").GetInt32());
    }

    [Fact]
    public async Task HttpError_UsesConfiguredErrorMapping()
    {
        var handler = new StubHandler
        {
            Responder = (_, _, _) => Task.FromResult(StubHandler.Json(HttpStatusCode.NotFound, """{"detail":"nope"}""")),
        };
        var definition = Definition(d =>
            d.ErrorMappingJson = """{"404":"No se encontró el expediente.","default":"Error del registro."}""");

        var result = await Connector(handler).InvokeAsync(
            Request(definition, Connection(), """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.HttpError, result.Outcome);
        Assert.Equal(404, result.HttpStatus);
        Assert.Equal("No se encontró el expediente.", result.UserFacingError);
    }

    [Fact]
    public async Task TransientGetFailure_RetriesOnce()
    {
        var handler = new StubHandler
        {
            Responder = (_, attempt, _) => Task.FromResult(attempt == 1
                ? StubHandler.Json(HttpStatusCode.ServiceUnavailable, """{"busy":true}""")
                : StubHandler.Json(HttpStatusCode.OK, """{"ok":true}""")),
        };

        var result = await Connector(handler).InvokeAsync(
            Request(Definition(), Connection(), """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.Success, result.Outcome);
        Assert.Equal(1, result.RetryCount);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NonGetMethod_DoesNotRetry()
    {
        var handler = new StubHandler
        {
            Responder = (_, _, _) => Task.FromResult(StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")),
        };
        var definition = Definition(d =>
        {
            d.HttpMethod = "POST";
            d.BodyTemplateJson = """{"n":"{numero}"}""";
        });

        var result = await Connector(handler).InvokeAsync(
            Request(definition, Connection(), """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.HttpError, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Timeout_MapsToTimeoutOutcome()
    {
        var handler = new StubHandler
        {
            Responder = async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return StubHandler.Json(HttpStatusCode.OK, "{}");
            },
        };
        var connection = Connection(c => c.TimeoutMs = 200);

        var result = await Connector(handler).InvokeAsync(
            Request(Definition(), connection, """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task OversizedResponse_IsRejected()
    {
        var big = "{\"data\":\"" + new string('x', 10000) + "\"}";
        var handler = new StubHandler
        {
            Responder = (_, _, _) => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, big)),
        };
        var connection = Connection(c => c.MaxResponseBytes = 1024);

        var result = await Connector(handler).InvokeAsync(
            Request(Definition(), connection, """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.ConnectorError, result.Outcome);
        Assert.Equal("response_too_large", result.ErrorCategory);
    }

    [Fact]
    public async Task Redirect_IsNotFollowed()
    {
        var handler = new StubHandler
        {
            Responder = (_, _, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://evil.test/steal");
                return Task.FromResult(response);
            },
        };

        var result = await Connector(handler).InvokeAsync(
            Request(Definition(), Connection(), """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.HttpError, result.Outcome);
        Assert.Equal(302, result.HttpStatus);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EgressBlocked_NeverReachesNetwork()
    {
        var handler = new StubHandler();
        var result = await Connector(handler, new DenyAllEgress()).InvokeAsync(
            Request(Definition(), Connection(), """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.ConnectorError, result.Outcome);
        Assert.Equal("egress_blocked", result.ErrorCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ResponseMapping_ProjectsSelectedFields()
    {
        var handler = new StubHandler
        {
            Responder = (_, _, _) => Task.FromResult(StubHandler.Json(
                HttpStatusCode.OK,
                """{"data":{"estado":"aprobado","interno":"secreto"},"meta":{"page":1}}""")),
        };
        var definition = Definition(d =>
            d.ResponseMappingJson = """[{"path":"data.estado","name":"estado"}]""");

        var result = await Connector(handler).InvokeAsync(
            Request(definition, Connection(), """{"numero":"1"}"""),
            CancellationToken.None);

        Assert.Equal(ToolExecutionOutcome.Success, result.Outcome);
        var payload = result.Payload!.Value;
        Assert.Equal("aprobado", payload.GetProperty("estado").GetString());
        Assert.False(payload.TryGetProperty("data", out _));
    }
}
