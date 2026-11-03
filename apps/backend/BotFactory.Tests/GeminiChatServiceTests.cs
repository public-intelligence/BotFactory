using System.Net;
using System.Text;
using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BotFactory.Tests;

public class GeminiChatServiceTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Respond(request));
    }

    private sealed class StubAppSettings : IAppSettingService
    {
        public Task<string?> GetDecryptedKeyAsync(ServiceType serviceType) =>
            Task.FromResult<string?>("fake-api-key");

        public Task<List<AppSetting>> GetAllAsync() => Task.FromResult(new List<AppSetting>());

        public Task<AppSetting?> GetByServiceTypeAsync(ServiceType serviceType) => Task.FromResult<AppSetting?>(null);

        public Task<List<AppSetting>> BulkUpsertAsync(List<(ServiceType ServiceType, string ApiKey)> settings) =>
            Task.FromResult(new List<AppSetting>());

        public Task<bool> DeleteAsync(ServiceType serviceType) => Task.FromResult(false);
    }

    private sealed class StubToolHandler : IToolHandler
    {
        public string Name => "kb_search";

        public FunctionDeclaration Declaration => new()
        {
            Name = Name,
            Description = "Stub KB tool",
            Parameters = new JsonSchema
            {
                Type = "object",
                Properties = new() { ["query"] = new JsonSchema { Type = "string" } },
                Required = new[] { "query" },
            },
        };

        public Task<ToolInvocation> ExecuteAsync(JsonElement args, ToolContext context, CancellationToken ct) =>
            throw new NotImplementedException("StreamAsync tests do not exercise tool execution.");
    }

    private sealed class StubToolRegistry : IToolRegistry
    {
        private readonly GeminiToolGroup[] _payload;

        public StubToolRegistry(bool withTools)
        {
            _payload = withTools
                ? new[]
                {
                    new GeminiToolGroup
                    {
                        FunctionDeclarations = new[] { new StubToolHandler().Declaration },
                    },
                }
                : Array.Empty<GeminiToolGroup>();
        }

        public GeminiToolGroup[] BuildGeminiToolsPayload() => _payload;

        public IToolHandler? Get(string name) => null;
    }

    private static GeminiChatService CreateService(HttpMessageHandler handler, bool withTools = true) =>
        new(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) },
            new StubAppSettings(),
            Options.Create(new GeminiChatOptions()),
            NullLogger<GeminiChatService>.Instance,
            new StubToolRegistry(withTools));

    private static async Task<List<GeminiStreamEvent>> CollectStreamAsync(
        GeminiChatService svc,
        IReadOnlyList<GeminiExtraContent>? extras = null) =>
        await CollectStreamAsync(svc, "sys", [], extras ?? [], "user here");

    private static async Task<List<GeminiStreamEvent>> CollectStreamAsync(
        GeminiChatService svc,
        string systemInstruction,
        IReadOnlyList<GeminiChatMessage> history,
        IReadOnlyList<GeminiExtraContent> extraContents,
        string? newUserText)
    {
        var list = new List<GeminiStreamEvent>();
        await foreach (var ev in svc.StreamAsync(
                           systemInstruction,
                           history,
                           extraContents,
                           newUserText,
                           CancellationToken.None))
            list.Add(ev);

        return list;
    }

    [Fact]
    public async Task StreamAsync_YieldsTextDeltasFromSseChunks()
    {
        var capturing = new CapturingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    data: {"candidates":[{"content":{"parts":[{"text":"Hi"}]}}]}

                    data: {"candidates":[{"content":{"parts":[{"text":" there"}]}}]}

                    data: [DONE]

                    """,
                    Encoding.UTF8,
                    "text/event-stream"),
            },
        };

        var svc = CreateService(capturing);
        var list = await CollectStreamAsync(svc);

        var texts = list.OfType<GeminiTextDelta>().Select(d => d.Text).ToList();
        Assert.Equal(2, texts.Count);
        Assert.Equal("Hi", texts[0]);
        Assert.Equal(" there", texts[1]);
    }

    [Fact]
    public async Task StreamAsync_FunctionCallPart_YieldsGeminiFunctionCall()
    {
        var capturing = new CapturingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"kb_search","args":{"query":"q1"}}}]}}]}

                    """,
                    Encoding.UTF8,
                    "text/event-stream"),
            },
        };

        var svc = CreateService(capturing);
        var list = await CollectStreamAsync(svc);

        var fc = Assert.Single(list.OfType<GeminiFunctionCall>());
        Assert.Equal("kb_search", fc.Name);
        Assert.True(fc.Args.TryGetProperty("query", out var q));
        Assert.Equal("q1", q.GetString());
    }

    [Fact]
    public async Task StreamAsync_WithToolsRegistered_IncludesToolsAndToolConfigInPayload()
    {
        string? capturedBody = null;
        var capturing = new CapturingHandler
        {
            Respond = req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"x\"}]}}]}\n\n",
                        Encoding.UTF8,
                        "text/event-stream"),
                };
            },
        };

        var svc = CreateService(capturing, withTools: true);
        _ = await CollectStreamAsync(svc);

        Assert.NotNull(capturedBody);
        Assert.Contains("tools", capturedBody, StringComparison.Ordinal);
        Assert.Contains("functionDeclarations", capturedBody, StringComparison.Ordinal);
        Assert.Contains("kb_search", capturedBody, StringComparison.Ordinal);
        Assert.Contains("functionCallingConfig", capturedBody, StringComparison.Ordinal);
        Assert.Contains("AUTO", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_EmptyRegistry_OmitsToolsFromPayload()
    {
        string? capturedBody = null;
        var capturing = new CapturingHandler
        {
            Respond = req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"x\"}]}}]}\n\n",
                        Encoding.UTF8,
                        "text/event-stream"),
                };
            },
        };

        var svc = CreateService(capturing, withTools: false);
        _ = await CollectStreamAsync(svc);

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("\"tools\"", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ExtraContents_AppearInRequestPayload()
    {
        string? capturedBody = null;
        var extra = new List<GeminiExtraContent>
        {
            new(
                "model",
                new object[] { new { functionCall = new { name = "kb_search", args = new { query = "x" } } } }),
            new(
                "user",
                new object[]
                {
                    new
                    {
                        functionResponse = new
                        {
                            name = "kb_search",
                            response = new { results = Array.Empty<object>() },
                        },
                    },
                }),
        };

        var capturing = new CapturingHandler
        {
            Respond = req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"final\"}]}}]}\n\n",
                        Encoding.UTF8,
                        "text/event-stream"),
                };
            },
        };

        var svc = CreateService(capturing);
        _ = await CollectStreamAsync(svc, "sys", [], extra, null);

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        var contents = doc.RootElement.GetProperty("contents");
        Assert.True(contents.GetArrayLength() >= 2);
        Assert.Equal("model", contents[0].GetProperty("role").GetString());
        Assert.Equal("user", contents[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task StreamAsync_NewUserTextAndExtraContents_QuestionPrecedesExtraContents()
    {
        string? capturedBody = null;
        var extra = new List<GeminiExtraContent>
        {
            new(
                "model",
                new object[] { new { functionCall = new { name = "kb_search", args = new { query = "x" } } } }),
            new(
                "user",
                new object[]
                {
                    new
                    {
                        functionResponse = new
                        {
                            name = "kb_search",
                            response = new { results = Array.Empty<object>() },
                        },
                    },
                }),
        };

        var capturing = new CapturingHandler
        {
            Respond = req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"final\"}]}}]}\n\n",
                        Encoding.UTF8,
                        "text/event-stream"),
                };
            },
        };

        var svc = CreateService(capturing);
        _ = await CollectStreamAsync(svc, "sys", [], extra, "la pregunta actual");

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        var contents = doc.RootElement.GetProperty("contents");

        // When a turn has both a current question and tool call/response turns,
        // the question must come FIRST so the answer pass reads
        // [user:question, model:functionCall, user:functionResponse]. This locks
        // the newUserText-before-extraContents invariant from commit e42fff8.
        Assert.Equal(3, contents.GetArrayLength());
        Assert.Equal("user", contents[0].GetProperty("role").GetString());
        Assert.Equal(
            "la pregunta actual",
            contents[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("model", contents[1].GetProperty("role").GetString());
        Assert.Equal("user", contents[2].GetProperty("role").GetString());
    }

    [Fact]
    public void ParseStreamChunkEvents_MixedTextAndFunctionCall_EmitsInPartOrder()
    {
        var json = """{"candidates":[{"content":{"parts":[{"text":"a"},{"functionCall":{"name":"f","args":{}}}]}}]}""";
        using var doc = JsonDocument.Parse(json);
        var list = GeminiChatService.ParseStreamChunkEvents(doc.RootElement).ToList();

        Assert.Equal(2, list.Count);
        Assert.IsType<GeminiTextDelta>(list[0]);
        Assert.IsType<GeminiFunctionCall>(list[1]);
    }

    [Fact]
    public async Task StreamAsync_NonSuccess_Throws()
    {
        var handler = new CapturingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom", Encoding.UTF8, "text/plain"),
            },
        };

        var svc = CreateService(handler);
        var ex = await Assert.ThrowsAsync<ProviderServiceException>(async () =>
        {
            await foreach (var _ in svc.StreamAsync("s", [], [], "u", CancellationToken.None))
            {
            }
        });
        Assert.Equal(ProviderErrorClassifier.CodeProviderUnavailable, ex.Info.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_PreCancelled_ThrowsOperationCanceled()
    {
        var handler = new CapturingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"candidates\":[]}\n\n", Encoding.UTF8, "text/event-stream"),
            },
        };

        var svc = CreateService(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in svc.StreamAsync("s", [], [], "u", cts.Token))
            {
            }
        });
    }
}
