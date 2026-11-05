using System.Text;
using System.Text.Json.Serialization;
using BotFactory.Api.Auth;
using BotFactory.Api.Endpoints;
using BotFactory.Api.Hubs;
using BotFactory.Api.OpenApi;
using BotFactory.Api.Logging;
using BotFactory.Api.Middleware;
using BotFactory.Core.Auth;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Auth;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using BotFactory.Infrastructure.Services.Jobs;
using BotFactory.Infrastructure.Tenancy;
using BotFactory.Infrastructure.Tools;
using BotFactory.Infrastructure.Tools.Connectors;
using BotFactory.Infrastructure.Tools.Execution;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Compact;

// Load .env file if present (secrets live here, not in appsettings)
DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// File log: a single ~25MB window read back by /api/logs (admin log viewer).
// Registered as an EXTRA provider rather than replacing the default ones, so
// console output stays byte-identical — deploy/diagnose.sh greps docker logs
// for verbatim messages.
var fileLogger = new LoggerConfiguration()
    .MinimumLevel.Verbose() // category filtering happens in Microsoft.Extensions.Logging
    .WriteTo.File(
        new RenderedCompactJsonFormatter(),
        FileLog.SinkPath(builder.Configuration),
        fileSizeLimitBytes: FileLog.MaxSizeBytes,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 1)
    .CreateLogger();
// AddProvider, NOT AddSerilog(): the latter also installs an
// AddFilter<SerilogLoggerProvider>(LogLevel.Trace) rule, which pins the file
// sink at Trace for every category and makes it ignore Logging:LogLevel. That
// filled the admin log viewer with Kestrel/EF DEBUG noise while the console
// (default providers) stayed at Information. Registering the provider directly
// puts the file sink under the same Logging:LogLevel rules as everything else,
// so one env var (Logging__LogLevel__Default) governs both.
builder.Logging.AddProvider(new SerilogLoggerProvider(fileLogger, dispose: true));

// Global error handling
builder.Services.AddProblemDetails();

// The API always runs behind a TLS-terminating reverse proxy (Azure App Service's
// front end; Caddy in the Docker deployment), which forwards plain HTTP inward.
// Without this, Request.Scheme is "http" even though the browser is on https, so
// URLs the API hands back to the client (e.g. auth login URLs) come out http://.
//
// Only X-Forwarded-Proto is honoured. X-Forwarded-For is deliberately NOT enabled:
// it would rewrite Connection.RemoteIpAddress, which the rate limiter partitions on.
// KnownNetworks/KnownProxies are cleared because the proxy is never on loopback in
// either deployment; the header is trusted only to determine the request scheme.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// CORS
var allowedOrigins = builder.Configuration["AllowedOrigins"]?
    .Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// Database
builder.Services.AddDbContext<BotFactoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BotFactoryDb")));

// Tenancy
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());

// Auth config
var useFakeAuth = builder.Configuration.GetValue<bool>("Auth:UseFakeProvider");
var gobIdSection = builder.Configuration.GetSection("Auth:GobId");
var fakeIdSection = builder.Configuration.GetSection("Auth:FakeId");
var gobIdOptionsBuilder = builder.Services.AddOptions<GobIdOptions>()
    .BindConfiguration("Auth:GobId");
if (!useFakeAuth)
{
    gobIdOptionsBuilder.ValidateDataAnnotations().ValidateOnStart();
}

builder.Services.Configure<FakeIdOptions>(fakeIdSection);

var gobId = gobIdSection.Get<GobIdOptions>() ?? new GobIdOptions();
var fakeId = fakeIdSection.Get<FakeIdOptions>() ?? new FakeIdOptions();

var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");

// GOB-ID provider is always registered
builder.Services.AddHttpClient<GobIdAuthProvider>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });
builder.Services.AddScoped<IAuthProvider>(sp => sp.GetRequiredService<GobIdAuthProvider>());

startupLogger.LogInformation(
    "Auth: GOB-ID provider registered. ApiUrl={ApiUrl}, Audience={Audience}, AppId={AppId}",
    gobId.ApiUrl, gobId.Audience, gobId.ApplicationId);

// Fake provider only in dev
if (useFakeAuth)
{
    builder.Services.AddSingleton<FakeAuthProvider>();
    startupLogger.LogInformation("Auth: FakeAuthProvider registered (dev mode)");
}

// JWT validation params — used by SessionAuthHandler to validate stored tokens server-side.
// The browser never sees these tokens; it only has an opaque session cookie.
var signingKeys = new List<SecurityKey>();
var validIssuers = new List<string>();
var validAudiences = new List<string>();

if (!string.IsNullOrEmpty(gobId.AppSecret))
{
    var gobIdKeyBase64 = gobId.AppSecret.Replace('-', '+').Replace('_', '/');
    var padding = (4 - gobIdKeyBase64.Length % 4) % 4;
    var gobIdKeyBytes = Convert.FromBase64String(gobIdKeyBase64 + new string('=', padding));
    var gobIdKey = new SymmetricSecurityKey(gobIdKeyBytes);
    gobIdKey.KeyId = $"app:{gobId.ApplicationId}";
    signingKeys.Add(gobIdKey);
    validIssuers.Add(gobId.ApiUrl);
    validAudiences.Add(gobId.Audience);
    startupLogger.LogInformation("Auth: GOB-ID JWT validation configured (issuer={Issuer}, audience={Audience})", gobId.ApiUrl, gobId.Audience);
}

if (useFakeAuth)
{
    signingKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(fakeId.SigningKey)));
    validIssuers.Add(fakeId.Issuer);
    validAudiences.Add(fakeId.Audience);
    startupLogger.LogInformation("Auth: Fake-ID JWT validation configured (issuer={Issuer}, audience={Audience})", fakeId.Issuer, fakeId.Audience);
}

// Anonymous public-session tokens (stateless self-signed JWTs; see
// PublicTokenService). Validated by the same SessionAuthHandler pipeline.
var publicAccess = builder.Configuration.GetSection(PublicAccessOptions.SectionName).Get<PublicAccessOptions>()
    ?? new PublicAccessOptions();
if (string.IsNullOrEmpty(publicAccess.SigningKey))
{
    publicAccess.SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
    startupLogger.LogWarning(
        "PublicAccess:SigningKey not configured — generated a per-process key. " +
        "Anonymous sessions won't survive restarts or scale-out; set PublicAccess__SigningKey in production.");
}
var generatedPublicSigningKey = publicAccess.SigningKey;
builder.Services.AddOptions<PublicAccessOptions>()
    .BindConfiguration(PublicAccessOptions.SectionName)
    .PostConfigure(o =>
    {
        if (string.IsNullOrEmpty(o.SigningKey)) o.SigningKey = generatedPublicSigningKey;
    });
signingKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(publicAccess.SigningKey)));
validIssuers.Add(publicAccess.Issuer);
validAudiences.Add(publicAccess.Audience);
startupLogger.LogInformation("Auth: public-session JWT validation configured (issuer={Issuer}, ttl={Ttl}min)",
    publicAccess.Issuer, publicAccess.TokenTtlMinutes);

// Register TokenValidationParameters as singleton so SessionAuthHandler can access them
var tokenValidationParams = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidIssuers = validIssuers,
    ValidateAudience = true,
    ValidAudiences = validAudiences,
    ValidateIssuerSigningKey = true,
    IssuerSigningKeys = signingKeys,
    ValidateLifetime = true,
    NameClaimType = "sub",
};
builder.Services.AddSingleton(tokenValidationParams);

// Session-based auth: bf_session cookie → DB lookup → JWT validation server-side
builder.Services.AddAuthentication(SessionAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthHandler>(SessionAuthHandler.SchemeName, null);

builder.Services.AddAuthorization();

// Reject numeric strings like "123" for int/double properties. Frontend is
// TypeScript and sends real numbers; keeping the default web-mode leniency
// causes OpenAPI to emit `type: [integer, string]` unions on every numeric
// field, which leak into the generated client as `number | string`.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BinaryResponseTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<BotFactoryDbContext>();

// Services
builder.Services.AddScoped<IBotService, BotService>();
builder.Services.AddScoped<IBotThinkingSoundService, BotThinkingSoundService>();
builder.Services.AddScoped<IBotLogoService, BotLogoService>();
builder.Services.AddScoped<IOrgBrandingService, OrgBrandingService>();
builder.Services.AddScoped<IAppSettingService, AppSettingService>();
builder.Services.AddScoped<IKeyValueStore, KeyValueStore>();
builder.Services.AddHttpClient<ElevenLabsVoicesService>();
builder.Services.AddScoped<IKbDocumentService, KbDocumentService>();
builder.Services.AddOptions<KbScraperOptions>()
    .BindConfiguration("KbScraper");
builder.Services.AddHttpClient<IKbScraperClient, KbScraperClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<KbScraperOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
        client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + '/');
    // Cover scraper slot wait (~120s) + page render timeout.
    client.Timeout = TimeSpan.FromSeconds(Math.Max(180, opts.TimeoutSeconds + 130));
});
builder.Services.AddScoped<IUrlContentFetcher, UrlContentFetcher>();
builder.Services.AddHttpClient<GeminiEmbeddingService>();
builder.Services.AddOptions<GeminiChatOptions>()
    .BindConfiguration("Gemini");
builder.Services.AddOptions<ConversationLifecycleOptions>()
    .BindConfiguration(ConversationLifecycleOptions.SectionName);
builder.Services.AddHttpClient<GeminiChatService>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<GeminiChatOptions>>();
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.Value.HttpTimeoutSeconds));
});
builder.Services.AddScoped<IKbSearchService, KbSearchService>();
builder.Services.AddScoped<KbSearchService>();
builder.Services.AddScoped<IToolHandler, KbSearchToolHandler>();
builder.Services.AddScoped<IToolRegistry, ToolRegistry>();

// External tools (planning/external-tools-plan.md). ToolsetContext mirrors the
// TenantContext scoped-pair idiom; the registry composes built-ins + the
// current bot's configured tools per turn.
builder.Services.AddScoped<ToolsetContext>();
builder.Services.AddScoped<IToolsetContext>(sp => sp.GetRequiredService<ToolsetContext>());
builder.Services.AddScoped<IToolsetContextSetter>(sp => sp.GetRequiredService<ToolsetContext>());
builder.Services.AddScoped<ToolArgumentValidator>();
builder.Services.AddScoped<ToolPolicyEvaluator>();
builder.Services.AddScoped<IToolCredentialProvider, ToolCredentialProvider>();
builder.Services.AddScoped<IToolExecutionGateway, ToolExecutionGateway>();
builder.Services.AddScoped<IToolConnector, RestToolConnector>();
builder.Services.AddOptions<ToolEgressOptions>()
    .BindConfiguration(ToolEgressOptions.SectionName);
builder.Services.AddSingleton<IEgressGuard, EgressGuard>();
// Dedicated egress client: no redirects (a 3xx is an error, never followed),
// no cookies; per-call budgets come from connection TimeoutMs via CTS.
builder.Services.AddHttpClient(RestToolConnector.HttpClientName, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(60);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    });
builder.Services.AddScoped<KbIndexingOrchestrator>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddScoped<IConversationStreamPublisher, ConversationStreamPublisher>();
builder.Services.AddScoped<IChatTurnService, ChatTurnService>();
builder.Services.AddSingleton<IChatTurnRegistry, ChatTurnRegistry>();
builder.Services.AddScoped<IConversationGreetingService, ConversationGreetingService>();
// Two invocations at a time per connection on the conversation hub: SendTurn waits
// for the whole turn, and with the default of one, the CancelTurn sent to stop it
// queued behind it and arrived after the turn had ended. The turn registry still
// allows only one turn per connection.
builder.Services.AddSignalR()
    .AddHubOptions<ConversationHub>(options => options.MaximumParallelInvocationsPerClient = 2);

// LiveKit
builder.Services.AddOptions<LiveKitOptions>()
    .BindConfiguration("LiveKit")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<LiveKitService>();

// Public anonymous access: token minting + per-IP rate limiting for the
// /api/public and /api/embed surfaces. Per-bot/per-org fixed-window caps
// (IPublicRateLimiter) run inside the public endpoints after bot resolution.
builder.Services.AddSingleton<PublicTokenService>();
builder.Services.AddSingleton<IPublicRateLimiter, FixedWindowPublicRateLimiter>();
builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(PublicBotEndpoints.RateLimitPolicy, httpContext =>
    {
        var opts = httpContext.RequestServices
            .GetRequiredService<IOptions<PublicAccessOptions>>().Value;
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ip,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, opts.RequestsPerMinutePerIp),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
    // Persist per-IP rejections for the public-usage dashboard. Attributed to
    // the bot's org when the route carries a resolvable publicId; the client
    // IP itself is never stored.
    options.OnRejected = async (context, cancellationToken) =>
    {
        // What the visitor reads: the bot's own message when the route says which bot, the default otherwise.
        var message = BotFactory.Core.Entities.BotErrorMessages.RequestsLimitDefault;
        try
        {
            var services = context.HttpContext.RequestServices;
            if (context.HttpContext.Request.RouteValues.TryGetValue("publicId", out var pid)
                && pid is string publicId && !string.IsNullOrEmpty(publicId))
            {
                var bot = await services.GetRequiredService<IBotService>().GetByPublicIdAsync(publicId);
                if (bot is not null)
                {
                    message = BotFactory.Core.Entities.BotErrorMessages.Resolve(
                        bot.RateLimitMessage, BotFactory.Core.Entities.BotErrorMessages.RequestsLimitDefault);
                    services.GetRequiredService<ITenantContextSetter>().SetTenant(bot.OrganizationId);
                    await services.GetRequiredService<ITelemetryService>().EmitAsync(new TelemetryEmit(
                        "rate_limit_exceeded",
                        BotFactory.Core.Entities.EventSource.Backend,
                        BotId: bot.Id,
                        Summary: "Anonymous request rate-limited (per-IP cap)",
                        Data: System.Text.Json.JsonSerializer.SerializeToElement(new { scope = "ip" })), cancellationToken);
                }
            }
        }
        catch
        {
            // Telemetry on the rejection path is best-effort.
        }

        // The rejection used to carry no body, so the page could only say «Error al conectar».
        try
        {
            await context.HttpContext.Response.WriteAsJsonAsync(new { error = message }, cancellationToken);
        }
        catch
        {
            // The response may already be closed; the status code is what matters.
        }
    };
});

// Agent callbacks (/livekit/*) shared-secret gate
builder.Services.AddOptions<AgentCallbackOptions>()
    .BindConfiguration(AgentCallbackOptions.SectionName);
if (string.IsNullOrEmpty(builder.Configuration[$"{AgentCallbackOptions.SectionName}:SharedSecret"]))
{
    startupLogger.LogWarning(
        "AgentCallback:SharedSecret not configured — /livekit/* agent callbacks are UNAUTHENTICATED. " +
        "Set AgentCallback__SharedSecret (backend) and BACKEND_SHARED_SECRET (agent) before enabling public bots.");
}

// Shell fetch for the /api/embed/{publicId} route
builder.Services.AddHttpClient("embed-shell", client => client.Timeout = TimeSpan.FromSeconds(10));

// Support drop-box relay (see SystemLogEndpoints): generous timeout because a
// bundle can be 8 MB uploaded from a slow government uplink.
builder.Services.AddHttpClient("support-dropbox", client => client.Timeout = TimeSpan.FromSeconds(150));

// Encryption
builder.Services.AddOptions<EncryptionOptions>()
    .BindConfiguration("Encryption")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IEncryptionService, AesEncryptionService>();

// Blob Storage (Azure)
builder.Services.AddOptions<BlobStorageOptions>()
    .BindConfiguration("BlobStorage")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

// Background Jobs (Hangfire)
builder.Services.AddHangfire((sp, config) => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("BotFactoryDb"), new SqlServerStorageOptions
    {
        SchemaName = "HangFire",
        QueuePollInterval = TimeSpan.FromSeconds(15),
    })
    .UseFilter(new AutomaticRetryAttribute { Attempts = 3, DelaysInSeconds = [10, 60, 300] })
    .UseFilter(new JobLoggingFilter(sp.GetRequiredService<ILoggerFactory>())));
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Math.Min(Environment.ProcessorCount, 4);
});
builder.Services.AddScoped<IBackgroundJobService, HangfireBackgroundJobService>();
builder.Services.AddScoped<IndexDocumentJob>();
builder.Services.AddScoped<IngestUrlJob>();
builder.Services.AddScoped<RefreshUrlKbDocumentsJob>();
builder.Services.AddScoped<CloseStaleConversationsJob>();

var app = builder.Build();

// Auto-migrate and seed on startup
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
    var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, encryption, app.Logger);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Auto-migration failed. Is SQL Server running? Start it with: docker compose up -d");
}

// Must run before anything that reads the request scheme.
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
// Public-session tokens must never reach authenticated surfaces; runs after
// authentication (needs the principal) and before authorization/endpoints.
app.UseMiddleware<PublicTokenScopeMiddleware>();
app.UseAuthorization();
app.UseHangfireDashboard("/api/tasks", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthFilter()],
    DashboardTitle = "Bot Factory Jobs",
});
app.UseMiddleware<TenantResolutionMiddleware>();

// OpenAPI spec + Scalar UI
app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}

// Endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
            }),
            totalDuration = report.TotalDuration,
        };
        await context.Response.WriteAsJsonAsync(result);
    }
}).ExcludeFromDescription();
var api = app.MapGroup("/api");
api.MapBotEndpoints();
api.MapPublicBotEndpoints();
api.MapEmbedEndpoints();
api.MapBotThinkingSoundEndpoints();
api.MapBotThinkingSoundPublicEndpoints();
api.MapBotLogoEndpoints();
api.MapBotLogoPublicEndpoints();
api.MapKbDocumentEndpoints();
api.MapAppSettingEndpoints();
api.MapOrgBrandingEndpoints();
api.MapOrgBrandingPublicEndpoints();
api.MapToolConnectionEndpoints();
api.MapToolDefinitionEndpoints();
api.MapBotToolEndpoints();
api.MapVoiceEndpoints();
api.MapDebugEndpoints();
api.MapAuditLogEndpoints();
api.MapConversationEndpoints();
api.MapTelemetryEndpoints();
api.MapSystemLogEndpoints();
api.MapAuthEndpoints();
api.MapDevAuthEndpoints();
app.MapLiveKitEndpoints();
app.MapHub<ConversationHub>("/hubs/conversation").RequireAuthorization();

// Idle conversations stay "active" (and count against the per-bot cap of anonymous
// sessions) until this sweep closes them, so it runs every few minutes, not hourly.
var staleSweepMinutes = Math.Clamp(
    app.Services.GetRequiredService<IOptions<ConversationLifecycleOptions>>().Value.StaleSweepMinutes, 1, 59);
RecurringJob.AddOrUpdate<CloseStaleConversationsJob>(
    "close-stale-conversations",
    job => job.ExecuteAsync(CancellationToken.None),
    Cron.MinuteInterval(staleSweepMinutes));

RecurringJob.AddOrUpdate<RefreshUrlKbDocumentsJob>(
    "refresh-url-kb-documents",
    job => job.ExecuteAsync(CancellationToken.None),
    "0 5 * * 1");

// SPA fallback — Angular handles client-side routing
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
