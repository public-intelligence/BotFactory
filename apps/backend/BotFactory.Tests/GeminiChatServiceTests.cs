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

    private static GeminiChatService CreateService(
        HttpMessageHandler handler,
        bool withTools = true,
        GeminiChatOptions? options = null) =>
        new(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) },
            new StubAppSettings(),
            Options.Create(options ?? new GeminiChatOptions()),
            NullLogger<GeminiChatService>.Instance,
            new StubToolRegistry(withTools));

    /// <summary>A response body that never sends anything: a stalled Gemini stream.</summary>
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<string> CaptureRequestBodyAsync(ToolChoice toolChoice)
    {
        string? body = null;
        var handler = new CapturingHandler
        {
            Respond = req =>
            {
                body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]}}]}\n\n",
                        Encoding.UTF8,
                        "text/event-stream"),
                };
            },
        };

        var svc = CreateService(handler);
        await foreach (var _ in svc.StreamAsync("s", [], [], "u", CancellationToken.None, toolChoice))
        {
        }

        return body!;
    }

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
    public void ParseStreamChunkEvents_UsageMetadata_IsReadWithAllFourCounts()
    {
        var json = """{"candidates":[{"content":{"parts":[{"text":"a"}]}}],"usageMetadata":{"promptTokenCount":1200,"candidatesTokenCount":20,"totalTokenCount":1230,"cachedContentTokenCount":1000,"thoughtsTokenCount":10}}""";
        using var doc = JsonDocument.Parse(json);
        var list = GeminiChatService.ParseStreamChunkEvents(doc.RootElement).ToList();

        // Usage comes first, before the parts: the turn stops reading at a function
        // call and must already have it by then.
        Assert.Equal(2, list.Count);
        var usage = Assert.IsType<GeminiUsage>(list[0]);
        Assert.Equal(1200, usage.PromptTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(1000, usage.CachedTokens);
        Assert.Equal(10, usage.ThoughtsTokens);
        Assert.Equal(1230, usage.TotalTokens);
        Assert.Equal(83, usage.CachedPercent);
        Assert.IsType<GeminiTextDelta>(list[1]);
    }

    [Fact]
    public void ParseStreamChunkEvents_UsageMetadata_MissingCountsReadAsZero()
    {
        // Gemini omits a count when it is zero: no cache hit, thinking off.
        var json = """{"candidates":[{"content":{"parts":[{"text":"a"}]}}],"usageMetadata":{"promptTokenCount":500,"candidatesTokenCount":7,"totalTokenCount":507}}""";
        using var doc = JsonDocument.Parse(json);
        var usage = Assert.IsType<GeminiUsage>(GeminiChatService.ParseStreamChunkEvents(doc.RootElement).First());

        Assert.Equal(500, usage.PromptTokens);
        Assert.Equal(0, usage.CachedTokens);
        Assert.Equal(0, usage.ThoughtsTokens);
        Assert.Equal(0, usage.CachedPercent);
        Assert.Equal("in=500 cached=0 (0%) out=7 thoughts=0 total=507", usage.Describe());
    }

    [Fact]
    public void ParseStreamChunkEvents_FinalChunkWithOnlyUsage_StillYieldsUsage()
    {
        var json = """{"usageMetadata":{"promptTokenCount":900,"candidatesTokenCount":12,"totalTokenCount":912}}""";
        using var doc = JsonDocument.Parse(json);
        var list = GeminiChatService.ParseStreamChunkEvents(doc.RootElement).ToList();

        var usage = Assert.IsType<GeminiUsage>(Assert.Single(list));
        Assert.Equal(900, usage.PromptTokens);
    }

    [Fact]
    public void ParseStreamChunkEvents_NoUsageMetadata_YieldsNoUsage()
    {
        var json = """{"candidates":[{"content":{"parts":[{"text":"a"}]}}]}""";
        using var doc = JsonDocument.Parse(json);
        var list = GeminiChatService.ParseStreamChunkEvents(doc.RootElement).ToList();

        Assert.DoesNotContain(list, e => e is GeminiUsage);
    }

    [Fact]
    public async Task StreamAsync_UsageMetadata_ReachesTheConsumerOnEveryChunk()
    {
        var sse =
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"a\"}]}}],\"usageMetadata\":{\"promptTokenCount\":100,\"candidatesTokenCount\":1,\"totalTokenCount\":101}}\n\n" +
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"b\"}]}}],\"usageMetadata\":{\"promptTokenCount\":100,\"candidatesTokenCount\":5,\"totalTokenCount\":105,\"cachedContentTokenCount\":80}}\n\n";
        var handler = new CapturingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
            },
        };

        var svc = CreateService(handler);
        var usages = new List<GeminiUsage>();
        await foreach (var ev in svc.StreamAsync("s", [], [], "u", CancellationToken.None))
        {
            if (ev is GeminiUsage u)
                usages.Add(u);
        }

        // Running counts: the last event is the one that describes the call.
        Assert.Equal(2, usages.Count);
        Assert.Equal(5, usages[^1].OutputTokens);
        Assert.Equal(80, usages[^1].CachedTokens);
    }

    [Fact]
    public void ParseStreamChunkEvents_FinishReason_IsYieldedAfterTheParts()
    {
        var json = """{"candidates":[{"content":{"parts":[{"text":"info@pgr.gob"}]},"finishReason":"MAX_TOKENS"}]}""";
        using var doc = JsonDocument.Parse(json);
        var list = GeminiChatService.ParseStreamChunkEvents(doc.RootElement).ToList();

        Assert.Equal(2, list.Count);
        Assert.IsType<GeminiTextDelta>(list[0]);
        Assert.Equal("MAX_TOKENS", Assert.IsType<GeminiFinish>(list[1]).Reason);
    }

    [Fact]
    public void ParseStreamChunkEvents_FinishReasonWithoutParts_IsStillYielded()
    {
        var json = """{"candidates":[{"finishReason":"STOP"}]}""";
        using var doc = JsonDocument.Parse(json);
        var list = GeminiChatService.ParseStreamChunkEvents(doc.RootElement).ToList();

        Assert.Equal("STOP", Assert.IsType<GeminiFinish>(Assert.Single(list)).Reason);
    }

    [Fact]
    public void ParseStreamChunkEvents_SeveralFunctionCallsInOneAnswer_AreAllYieldedInOrder()
    {
        // Gemini answers a multi-question message with several calls at once.
        var json = """{"candidates":[{"content":{"parts":[{"functionCall":{"name":"medicamentos","args":{"q":"losartan"}}},{"functionCall":{"name":"lugares","args":{"q":"consulado"}}},{"functionCall":{"name":"proximo_feriado","args":{}}}]}}]}""";
        using var doc = JsonDocument.Parse(json);
        var calls = GeminiChatService.ParseStreamChunkEvents(doc.RootElement).OfType<GeminiFunctionCall>().ToList();

        Assert.Equal(["medicamentos", "lugares", "proximo_feriado"], calls.Select(c => c.Name));
    }

    [Theory]
    [InlineData(ToolChoice.Auto, "AUTO")]
    [InlineData(ToolChoice.None, "NONE")]
    [InlineData(ToolChoice.Required, "ANY")]
    public async Task StreamAsync_ToolChoice_IsSentAsFunctionCallingMode(ToolChoice toolChoice, string expectedMode)
    {
        var body = await CaptureRequestBodyAsync(toolChoice);
        using var doc = JsonDocument.Parse(body);

        var mode = doc.RootElement.GetProperty("toolConfig").GetProperty("functionCallingConfig").GetProperty("mode").GetString();
        Assert.Equal(expectedMode, mode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 503)]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    public async Task StreamAsync_NonSuccess_CarriesTheHttpStatus(HttpStatusCode status, int expected)
    {
        var handler = new CapturingHandler
        {
            Respond = _ => new HttpResponseMessage(status)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            },
        };

        var svc = CreateService(handler);
        var ex = await Assert.ThrowsAsync<ProviderServiceException>(async () =>
        {
            await foreach (var _ in svc.StreamAsync("s", [], [], "u", CancellationToken.None))
            {
            }
        });
        Assert.Equal(expected, ex.StatusCode);
    }

    [Fact]
    public async Task StreamAsync_StalledStream_ThrowsTimeoutException_NotAHang()
    {
        // HttpClient.Timeout stops guarding once headers arrive; the call's own limit
        // must cover the body too, and surface as a timeout (retryable), not a barge-in.
        var handler = new CapturingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StallingStream()),
            },
        };

        var svc = CreateService(handler, options: new GeminiChatOptions { HttpTimeoutSeconds = 1 });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var _ in svc.StreamAsync("s", [], [], "u", CancellationToken.None))
            {
            }
        });
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task StreamAsync_CallerCancellation_StaysACancellation()
    {
        var handler = new CapturingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StallingStream()),
            },
        };

        var svc = CreateService(handler, options: new GeminiChatOptions { HttpTimeoutSeconds = 30 });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in svc.StreamAsync("s", [], [], "u", cts.Token))
            {
            }
        });
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
