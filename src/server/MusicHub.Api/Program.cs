using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MusicHub.Api;
using MusicHub.Api.Endpoints;
using MusicHub.Api.Realtime;
using MusicHub.Api.Security;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Library;
using MusicHub.Infrastructure.Maintenance;
using MusicHub.Infrastructure.Media;
using MusicHub.Infrastructure.Options;
using MusicHub.Infrastructure.Security;
using MusicHub.Infrastructure.Storage;
using MusicHub.Infrastructure.Telegram;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = builder.Environment.IsDevelopment();
    options.ValidateOnBuild = builder.Environment.IsDevelopment();
});

// The data root has to be known before anything else: the installer-written database credential
// lives under it, and the data-protection keyring that unprotects that credential lives there too.
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
var dataRoot = ConnectionStringResolver.ResolveDataRoot(
    storageOptions.RootPath,
    builder.Environment.IsDevelopment()
        ? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "..", "storage")
        : Path.Combine(builder.Environment.ContentRootPath, "storage"));
storageOptions.RootPath = dataRoot;

var dataProtectionProvider = DataProtectionSetup.Create(dataRoot);

var connectionString = ConnectionStringResolver.ResolveForHost(
    builder.Environment.EnvironmentName,
    dataRoot,
    builder.Configuration.GetConnectionString("Default"),
    dataProtectionProvider);

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
authOptions.JwtSecret = Environment.GetEnvironmentVariable("MUSICHUB_JWT_SECRET") ?? authOptions.JwtSecret;
if (string.IsNullOrWhiteSpace(authOptions.JwtSecret) || authOptions.JwtSecret.Length < 32)
{
    throw new InvalidOperationException("MUSICHUB_JWT_SECRET must be configured with at least 32 characters. Generate one with a cryptographically secure random generator.");
}

// Malware scanning and tool paths are configured after the root is known.
storageOptions.RequireMalwareScan = bool.TryParse(Environment.GetEnvironmentVariable("MUSICHUB_REQUIRE_MALWARE_SCAN"), out var requireScan) ? requireScan : storageOptions.RequireMalwareScan;
storageOptions.ClamAvHost = Environment.GetEnvironmentVariable("MUSICHUB_CLAMAV_HOST") ?? storageOptions.ClamAvHost;
storageOptions.FfprobePath = Environment.GetEnvironmentVariable("MUSICHUB_FFPROBE_PATH") ?? storageOptions.FfprobePath;
storageOptions.FfmpegPath = Environment.GetEnvironmentVariable("MUSICHUB_FFMPEG_PATH") ?? storageOptions.FfmpegPath;

builder.Services.Configure<AuthOptions>(o => CopyAuth(authOptions, o));
builder.Services.Configure<StorageOptions>(o => CopyStorage(storageOptions, o));

// Register the provider instance that was already used to read the database credential so the
// whole host shares one keyring instead of loading the key directory a second time.
builder.Services.AddSingleton(dataProtectionProvider);
builder.Services.AddDataProtection()
    .SetApplicationName(DatabaseCredentialFile.DataProtectionApplicationName);

builder.Services.AddDbContext<MusicHubDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsAssembly(typeof(MusicHubDbContext).Assembly.FullName);
        npgsql.CommandTimeout(30);
    });
    options.EnableDetailedErrors(false);
    options.EnableSensitiveDataLogging(false);
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = Math.Min(storageOptions.MaxUploadBytes, 4L * 1024 * 1024 * 1024);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.AddServerHeader = false;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = Math.Min(storageOptions.MaxUploadBytes, 4L * 1024 * 1024 * 1024);
    options.ValueLengthLimit = 1024 * 1024;
    options.MultipartHeadersLengthLimit = 64 * 1024;
    options.MemoryBufferThreshold = 64 * 1024;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ContentAddressedStorage>();
builder.Services.AddSingleton<LibraryLocationStore>();
builder.Services.AddSingleton<AudioProbeService>();
builder.Services.AddSingleton<IMalwareScanner, ClamAvMalwareScanner>();
builder.Services.AddSingleton<TelegramSecretProtector>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new TelegramBotClient(new HttpClient(new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    ConnectTimeout = TimeSpan.FromSeconds(15)
})
{
    Timeout = TimeSpan.FromMinutes(5)
}));
builder.Services.AddScoped<TelegramBotStateStore>();
builder.Services.AddScoped<TelegramBotStatusService>();
builder.Services.AddScoped<TelegramStorageService>();
builder.Services.AddScoped<TelegramAlbumPromptStore>();
builder.Services.AddScoped<TelegramAlbumEditStore>();
builder.Services.AddScoped<AlbumService>();
builder.Services.AddScoped<LibraryReportService>();
builder.Services.AddScoped<TelegramBotHandler>();
builder.Services.AddScoped<TelegramUpdateRouter>();
builder.Services.AddScoped<TelegramIngestionService>();
builder.Services.AddScoped<TelegramPublishService>();
builder.Services.AddScoped<StorageIntegrityService>();
builder.Services.AddScoped<StorageAdminService>();
builder.Services.AddScoped<LibraryRelocationService>();
builder.Services.AddScoped<TrackImportService>();
builder.Services.AddScoped<UserEntitlementService>();
builder.Services.AddScoped<ITrackImportListener, RealtimeImportListener>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<RealtimeNotifier>();
builder.Services.AddSingleton<StreamTracker>();
builder.Services.AddSingleton<ListeningTimeTracker>();
builder.Services.AddSingleton<StreamTokenService>();
builder.Services.AddHostedService<AccountExpirationSweepWorker>();
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://127.0.0.1:5173", "http://localhost:4173", "http://127.0.0.1:4173"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("dashboard", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Content-Range", "Accept-Ranges", "Content-Length", "ETag", "Content-Disposition")
        .AllowCredentials());
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = authOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = authOptions.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSecret)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            if (principal is null)
            {
                context.Fail("Missing principal.");
                return;
            }
            var rawId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
            var stamp = principal.FindFirstValue("security_stamp");
            if (!Guid.TryParse(rawId, out var userId) || string.IsNullOrWhiteSpace(stamp))
            {
                context.Fail("Invalid token subject.");
                return;
            }
            var db = context.HttpContext.RequestServices.GetRequiredService<MusicHubDbContext>();
            var state = await db.Users.AsNoTracking().Where(x => x.Id == userId)
                .Select(x => new
                {
                    x.IsActive,
                    x.SecurityStamp,
                    x.ExpiresAt
                }).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (state is null || !state.IsActive || !string.Equals(state.SecurityStamp, stamp, StringComparison.Ordinal))
            {
                context.Fail("Session revoked.");
                return;
            }

            // Administrators are exempt. Server system clock (DateTimeOffset.UtcNow) is used to verify expiry on every request,
            // immediately invalidating access tokens when the access period has concluded.
            if (!principal.IsInRole(RoleNames.Administrator))
            {
                var now = DateTimeOffset.UtcNow;
                if (state.ExpiresAt is { } expiresAt && expiresAt <= now)
                {
                    var entitlements = context.HttpContext.RequestServices.GetRequiredService<UserEntitlementService>();
                    await entitlements.DeactivateAsync(userId, DeactivationReasons.Expired, context.HttpContext.RequestAborted);
                    context.Fail("Session revoked.");
                }
            }
        },
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/library"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole(RoleNames.Administrator));
    options.AddPolicy("Contributor", policy => policy.RequireRole(RoleNames.Administrator, RoleNames.Contributor));
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Configurable so shared-IP deployments and the integration suite can raise it without
    // changing code. Production keeps the strict default of 10 sign-in attempts per minute.
    var loginPermitLimit = Math.Max(1, builder.Configuration.GetValue("RateLimiting:LoginPermitLimit", 10));
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        "login:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    // Password changes are authenticated but still worth throttling: a stolen token must not
    // become an online oracle for brute-forcing the current password.
    var passwordPermitLimit = Math.Max(1, builder.Configuration.GetValue("RateLimiting:PasswordPermitLimit", 10));
    options.AddPolicy("password", http => RateLimitPartition.GetFixedWindowLimiter(
        "password:" + (http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = passwordPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    // Also configurable. Uploads are expensive, so production keeps 20 per minute per account, but
    // the integration suite runs many uploads through one account and would hit that ceiling on a
    // fast machine - the whole suite finished in 33 s on CI against 8 minutes locally, which is
    // exactly when a fixed ceiling starts failing tests for reasons that have nothing to do with
    // what they assert.
    var uploadPermitLimit = Math.Max(1, builder.Configuration.GetValue("RateLimiting:UploadPermitLimit", 20));
    // The global per-IP ceiling has the same problem: a whole test run is one client address.
    var globalPermitLimit = Math.Max(1, builder.Configuration.GetValue("RateLimiting:GlobalPermitLimit", 600));
    options.AddPolicy("refresh", http => RateLimitPartition.GetFixedWindowLimiter(
        "refresh:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("setup", http => RateLimitPartition.GetFixedWindowLimiter(
        "setup:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("pairing", http => RateLimitPartition.GetFixedWindowLimiter(
        "pairing:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("upload", http => RateLimitPartition.GetFixedWindowLimiter(
        "upload:" + (http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = uploadPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            "global:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 100, AutoReplenishment = true }));
});

var app = builder.Build();

// Forwarded headers must be processed before anything that reads the client identity: the rate
// limiter partitions on RemoteIpAddress, and pairing/audit records the same value (B-021).
// Trust is restricted to explicitly configured proxies instead of the framework's blanket
// "trust every peer" behaviour, so a direct request cannot forge X-Forwarded-For to evade the
// per-IP rate limit or poison audit records.
var forwardedHeadersOptions = ForwardedHeadersSetup.Configure(app.Configuration, out var forwardedHeadersConfigured);
if (forwardedHeadersConfigured)
{
    app.UseForwardedHeaders(forwardedHeadersOptions);
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var (status, title) = exception switch
        {
            ApiException api => (api.StatusCode, api.Message),
            ApiFailureException failure => (failure.StatusCode, failure.Message),
            MediaValidationException media => (media is UploadTooLargeException ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status422UnprocessableEntity, media.Message),
            BadHttpRequestException bad => (bad.StatusCode, "The request is invalid."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Access denied."),
            OperationCanceledException => (499, "Request cancelled."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected server error occurred.")
        };
        if (status >= 500 && exception is not OperationCanceledException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled").LogError(exception, "Unhandled request failure");
        }
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            traceId = context.TraceIdentifier
        });
    });
});

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; font-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
    await next();
});

app.UseCors("dashboard");
// Authentication must run before the rate limiter: the "password" and "upload" policies partition on
// the authenticated user id, and before UseAuthentication the principal is the anonymous default, so
// every caller collapsed into one per-IP bucket (CR-002). That made the documented per-account
// guarantee ("a stolen token must not become an online oracle") untrue and let one user exhaust
// another's budget behind a shared NAT or proxy.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

var defaultFiles = new DefaultFilesOptions();
app.UseDefaultFiles(defaultFiles);
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
        else
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=604800";
        }
    }
});

app.MapOpenApi();
app.MapHealthChecks("/healthz");
app.MapHealthChecks("/api/healthz");
app.MapHub<LibraryHub>("/hubs/library");

app.MapGet("/", () =>
{
    var index = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
    return File.Exists(index) ? Results.Redirect("/index.html") : Results.Ok(new { name = "MusicHub Enterprise API", version = "1.0.0", status = "running" });
});

app.MapGet("/api/version", () => Results.Ok(new
{
    name = "MusicHub Enterprise",
    version = "1.0.0",
    environment = app.Environment.EnvironmentName,
    utc = DateTimeOffset.UtcNow
}));

app.MapSetupEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapHealthEndpoints();
app.MapLibraryEndpoints();
app.MapUploadEndpoints();
app.MapStreamEndpoints();
app.MapDashboardEndpoints();
app.MapPairingEndpoints();
app.MapTelegramEndpoints();
app.MapSettingsEndpoints();
app.MapStorageEndpoints();

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

static void CopyAuth(AuthOptions source, AuthOptions target)
{
    target.Issuer = source.Issuer;
    target.Audience = source.Audience;
    target.JwtSecret = source.JwtSecret;
    target.AccessTokenMinutes = source.AccessTokenMinutes;
    target.RefreshTokenDays = source.RefreshTokenDays;
    target.MaximumFailedLogins = source.MaximumFailedLogins;
    target.LockoutMinutes = source.LockoutMinutes;
    target.PairingCodeMinutes = source.PairingCodeMinutes;
}

static void CopyStorage(StorageOptions source, StorageOptions target)
{
    target.RootPath = source.RootPath;
    target.MaxUploadBytes = source.MaxUploadBytes;
    target.DefaultUserQuotaBytes = source.DefaultUserQuotaBytes;
    target.RequireMalwareScan = source.RequireMalwareScan;
    target.ClamAvHost = source.ClamAvHost;
    target.ClamAvPort = source.ClamAvPort;
    target.FfprobeTimeoutSeconds = source.FfprobeTimeoutSeconds;
    target.FfprobePath = source.FfprobePath;
    target.FfmpegPath = source.FfmpegPath;
}

public partial class Program;
