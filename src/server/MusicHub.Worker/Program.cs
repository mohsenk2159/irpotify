using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Library;
using MusicHub.Infrastructure.Maintenance;
using MusicHub.Infrastructure.Media;
using MusicHub.Infrastructure.Options;
using MusicHub.Infrastructure.Security;
using MusicHub.Infrastructure.Storage;
using MusicHub.Infrastructure.Telegram;
using MusicHub.Worker;

var builder = Host.CreateApplicationBuilder(args);

var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
var dataRoot = ConnectionStringResolver.ResolveDataRoot(storageOptions.RootPath, AppContext.BaseDirectory + "storage");
storageOptions.RootPath = dataRoot;

// Same keyring and application name as the API, so the credential the installer wrote is
// readable here too. A different application name would derive different keys and fail.
var dataProtectionProvider = DataProtectionSetup.Create(dataRoot);

var connectionString = ConnectionStringResolver.ResolveForHost(
    builder.Environment.EnvironmentName,
    dataRoot,
    builder.Configuration.GetConnectionString("Default"),
    dataProtectionProvider);

storageOptions.FfprobePath = Environment.GetEnvironmentVariable("MUSICHUB_FFPROBE_PATH") ?? storageOptions.FfprobePath;
storageOptions.FfmpegPath = Environment.GetEnvironmentVariable("MUSICHUB_FFMPEG_PATH") ?? storageOptions.FfmpegPath;
storageOptions.RequireMalwareScan = bool.TryParse(Environment.GetEnvironmentVariable("MUSICHUB_REQUIRE_MALWARE_SCAN"), out var requireScan) ? requireScan : storageOptions.RequireMalwareScan;
storageOptions.ClamAvHost = Environment.GetEnvironmentVariable("MUSICHUB_CLAMAV_HOST") ?? storageOptions.ClamAvHost;

builder.Services.Configure<StorageOptions>(o =>
{
    o.RootPath = storageOptions.RootPath;
    o.MaxUploadBytes = storageOptions.MaxUploadBytes;
    o.DefaultUserQuotaBytes = storageOptions.DefaultUserQuotaBytes;
    o.RequireMalwareScan = storageOptions.RequireMalwareScan;
    o.ClamAvHost = storageOptions.ClamAvHost;
    o.ClamAvPort = storageOptions.ClamAvPort;
    o.FfprobeTimeoutSeconds = storageOptions.FfprobeTimeoutSeconds;
    o.FfprobePath = storageOptions.FfprobePath;
    o.FfmpegPath = storageOptions.FfmpegPath;
});

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

var dataProtectionKeys = Path.Combine(storageOptions.RootPath, "keys", "dataprotection");
Directory.CreateDirectory(dataProtectionKeys);
// Register the instance created above so the worker shares one keyring with the credential read.
builder.Services.AddSingleton(dataProtectionProvider);
builder.Services.AddDataProtection()
    .SetApplicationName(DatabaseCredentialFile.DataProtectionApplicationName);

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
builder.Services.AddScoped<ITrackImportListener, NullTrackImportListener>();
builder.Services.AddScoped<TrackImportService>();
builder.Services.AddScoped<UserEntitlementService>();
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
builder.Services.AddHostedService<TelegramPollingWorker>();
builder.Services.AddHostedService<TelegramPublicationWorker>();
builder.Services.AddHostedService<EntitlementEnforcementWorker>();
builder.Services.AddHostedService<MaintenanceWorker>();

var host = builder.Build();
host.Run();
