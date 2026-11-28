using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Options;

namespace MusicHub.Api.Services;

public sealed record UpdateStatusDto(
    string CurrentVersion,
    string? CurrentCommitSha,
    string? LatestCommitSha,
    string? LatestCommitMessage,
    DateTimeOffset? LatestCommitDate,
    DateTimeOffset? LastCheckedAt,
    bool UpdateAvailable,
    string RepoUrl,
    string? LastError);

public sealed record ApplyUpdateResult(
    bool Applied,
    bool RequiresHostAction,
    string Message,
    string? CommandHint);

/// <summary>
/// Periodically checks the upstream GitHub repository (once every 24 hours) for new commits
/// and creates an administrator notification when an update is available.
/// </summary>
public sealed class GitHubUpdateService : BackgroundService
{
    private const string RepoOwner = "mohsenk2159";
    private const string RepoName = "irpotify";
    private const string RepoUrl = $"https://github.com/{RepoOwner}/{RepoName}";
    private const string SettingLastCheck = "update.lastCheck";
    private const string SettingNotifiedSha = "update.notifiedSha";
    private const string SettingCurrentSha = "update.currentSha";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GitHubUpdateService> _logger;
    private readonly StorageOptions _storageOptions;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    public GitHubUpdateService(
        IServiceScopeFactory scopeFactory,
        IOptions<StorageOptions> storageOptions,
        ILogger<GitHubUpdateService> logger)
    {
        _scopeFactory = scopeFactory;
        _storageOptions = storageOptions.Value;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("irpotify-server", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            await CheckNowAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial GitHub update check failed.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckNowAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Daily GitHub update check failed.");
            }
        }
    }

    public async Task<UpdateStatusDto> GetStatusAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var setting = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SettingLastCheck, ct);
        if (setting is not null)
        {
            try
            {
                var dto = JsonSerializer.Deserialize<UpdateStatusDto>(setting.Value);
                if (dto is not null) return dto;
            }
            catch
            {
                // Ignore malformed cached status and refresh
            }
        }

        var localSha = await ResolveCurrentShaAsync(db, ct);
        return new UpdateStatusDto("1.0.0", localSha, null, null, null, null, false, RepoUrl, null);
    }

    public async Task<UpdateStatusDto> CheckNowAsync(CancellationToken ct)
    {
        await _checkLock.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();

            var currentSha = await ResolveCurrentShaAsync(db, ct);
            string? latestSha = null;
            string? latestMessage = null;
            DateTimeOffset? latestDate = null;
            string? lastError = null;

            try
            {
                using var response = await _http.GetAsync(
                    $"https://api.github.com/repos/{RepoOwner}/{RepoName}/commits/main",
                    ct);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    var root = doc.RootElement;
                    latestSha = root.TryGetProperty("sha", out var shaProp) ? shaProp.GetString() : null;
                    if (root.TryGetProperty("commit", out var commitProp))
                    {
                        if (commitProp.TryGetProperty("message", out var msgProp))
                        {
                            latestMessage = msgProp.GetString()?.Split('\n')[0];
                        }
                        if (commitProp.TryGetProperty("committer", out var committerProp) &&
                            committerProp.TryGetProperty("date", out var dateProp) &&
                            DateTimeOffset.TryParse(dateProp.GetString(), out var parsedDate))
                        {
                            latestDate = parsedDate;
                        }
                    }
                }
                else
                {
                    lastError = $"GitHub API returned {(int)response.StatusCode} ({response.ReasonPhrase})";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex.Message;
            }

            var updateAvailable = !string.IsNullOrWhiteSpace(latestSha) &&
                !string.Equals(currentSha, latestSha, StringComparison.OrdinalIgnoreCase);

            var status = new UpdateStatusDto(
                CurrentVersion: "1.0.0",
                CurrentCommitSha: currentSha,
                LatestCommitSha: latestSha,
                LatestCommitMessage: latestMessage,
                LatestCommitDate: latestDate,
                LastCheckedAt: DateTimeOffset.UtcNow,
                UpdateAvailable: updateAvailable,
                RepoUrl: RepoUrl,
                LastError: lastError);

            await UpsertSettingAsync(db, SettingLastCheck, JsonSerializer.Serialize(status), ct);

            if (updateAvailable && !string.IsNullOrWhiteSpace(latestSha))
            {
                var notified = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == SettingNotifiedSha, ct);
                if (!string.Equals(notified?.Value, latestSha, StringComparison.OrdinalIgnoreCase))
                {
                    var shortSha = latestSha.Length > 7 ? latestSha[..7] : latestSha;
                    db.Notifications.Add(new SystemNotification
                    {
                        Type = "SystemUpdate",
                        Title = $"آپدیت جدید منتشر شد ({shortSha})",
                        Message = $"نسخه جدیدی در مخزن گیتهاب موجود است: {latestMessage ?? shortSha}. از بخش «آپدیت و پشتیبان‌گیری» ابتدا بکاپ بگیرید و سیستم را به‌روزرسانی کنید.",
                        IsRead = false,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    await UpsertSettingAsync(db, SettingNotifiedSha, latestSha, ct);
                }
            }

            await db.SaveChangesAsync(ct);
            return status;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async Task<ApplyUpdateResult> ApplyUpdateAsync(CancellationToken ct)
    {
        var repoRoot = FindGitRepositoryRoot();
        if (repoRoot is null)
        {
            try
            {
                Directory.CreateDirectory(_storageOptions.RootPath);
                var triggerPath = Path.Combine(_storageOptions.RootPath, "update_request.json");
                var payload = JsonSerializer.Serialize(new
                {
                    requestedAt = DateTimeOffset.UtcNow,
                    repoUrl = RepoUrl
                });
                await File.WriteAllTextAsync(triggerPath, payload, ct);

                return new ApplyUpdateResult(
                    Applied: true,
                    RequiresHostAction: false,
                    Message: "درخواست آپدیت خودکار برای سرویس میزبان ارسال شد. سرور ظرف ۱ تا ۲ دقیقه از گیتهاب به‌روزرسانی و مجدداً راه‌اندازی می‌شود.",
                    CommandHint: "irpotify update");
            }
            catch (Exception ex)
            {
                return new ApplyUpdateResult(
                    Applied: false,
                    RequiresHostAction: true,
                    Message: $"امکان ثبت درخواست آپدیت خودکار وجود نداشت ({ex.Message}). دستور زیر را در سرور اجرا کنید:",
                    CommandHint: "irpotify update");
            }
        }

        try
        {
            var (fetchExit, fetchOut) = await RunGitAsync(repoRoot, "fetch --all --prune", ct);
            if (fetchExit != 0)
            {
                return new ApplyUpdateResult(false, true, $"خطا در دریافت از گیتهاب: {fetchOut}", "irpotify update");
            }

            var (resetExit, resetOut) = await RunGitAsync(repoRoot, "reset --hard origin/main", ct);
            if (resetExit != 0)
            {
                return new ApplyUpdateResult(false, true, $"خطا در همگام‌سازی کدها: {resetOut}", "irpotify update");
            }

            var (_, newSha) = await RunGitAsync(repoRoot, "rev-parse HEAD", ct);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            await UpsertSettingAsync(db, SettingCurrentSha, newSha.Trim(), ct);
            await db.SaveChangesAsync(ct);
            await CheckNowAsync(ct);

            return new ApplyUpdateResult(
                Applied: true,
                RequiresHostAction: false,
                Message: $"سورس سرور با موفقیت به کامیت {newSha.Trim()[..Math.Min(7, newSha.Trim().Length)]} به‌روزرسانی شد.",
                CommandHint: null);
        }
        catch (Exception ex)
        {
            return new ApplyUpdateResult(false, true, $"امکان اجرای خودکار آپدیت وجود نداشت: {ex.Message}", "irpotify update");
        }
    }

    private async Task<string?> ResolveCurrentShaAsync(MusicHubDbContext db, CancellationToken ct)
    {
        var repoRoot = FindGitRepositoryRoot();
        if (repoRoot is not null)
        {
            try
            {
                var (code, output) = await RunGitAsync(repoRoot, "rev-parse HEAD", ct);
                if (code == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }
            }
            catch
            {
                // fall back to volume/AppSettings
            }
        }

        var shaFile = Path.Combine(_storageOptions.RootPath, "current_sha.txt");
        if (File.Exists(shaFile))
        {
            try
            {
                var sha = (await File.ReadAllTextAsync(shaFile, ct)).Trim();
                if (!string.IsNullOrWhiteSpace(sha)) return sha;
            }
            catch
            {
                // ignore
            }
        }

        var envSha = Environment.GetEnvironmentVariable("MUSICHUB_CURRENT_SHA");
        if (!string.IsNullOrWhiteSpace(envSha)) return envSha.Trim();

        var stored = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SettingCurrentSha, ct);
        return stored?.Value;
    }

    private static string? FindGitRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static async Task<(int exitCode, string output)> RunGitAsync(string workingDir, string args, CancellationToken ct)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
    }

    private static async Task UpsertSettingAsync(MusicHubDbContext db, string key, string value, CancellationToken ct)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
