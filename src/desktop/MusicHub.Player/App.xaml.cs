using System.IO;
using System.Windows;
using System.Windows.Threading;
using MusicHub.Player.Models;
using MusicHub.Player.Services;
using MusicHub.Player.Views;

namespace MusicHub.Player;

public partial class App : Application
{
    private PlayerApiClient? _api;

    private static string LogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicHub", "player.log");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, args) =>
        {
            Log("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                $"MusicHub Player hit an unexpected problem and will close.\n\n{args.Exception.Message}",
                "MusicHub Player", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("UnhandledException", args.ExceptionObject as Exception);

        Log("Startup", null);

        _api = new PlayerApiClient();
        var saved = SessionStore.Load();

        if (saved is not null && !string.IsNullOrWhiteSpace(saved.RefreshToken))
        {
            try
            {
                _api.ApplySession(saved);
                Log("Restoring saved session", null);
                var profile = await _api.GetMeAsync();
                Log($"Session restored for {profile.Email} canUsePlayer={profile.CanUsePlayer}", null);
                if (profile.CanUsePlayer)
                {
                    OpenMain();
                    return;
                }
                _api.ClearAuth();
                SessionStore.Clear();
            }
            catch (Exception ex)
            {
                Log("Silent session restore failed", ex);
                _api.ClearAuth();
            }
        }

        OpenLogin(saved);
    }

    private void OpenLogin(SavedSession? prefill)
    {
        var login = new LoginWindow(_api!, prefill);
        login.SignedIn += (_, _) =>
        {
            try
            {
                login.Close();
                OpenMain();
            }
            catch (Exception ex)
            {
                Log("OpenMain after sign-in failed", ex);
                MessageBox.Show(
                    $"Could not open the player window.\n\n{ex.Message}",
                    "MusicHub Player", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        };
        login.Closed += (_, _) => ShutdownIfEmpty();
        login.Show();
    }

    private void OpenMain()
    {
        Log("OpenMain enter", null);
        var main = new MainWindow(_api!);
        Log("MainWindow constructed", null);
        MainWindow = main;
        main.SignedOut += (_, _) =>
        {
            main.Close();
            OpenLogin(new SavedSession { ServerUrl = _api!.BaseUrl });
        };
        main.Closed += (_, _) => ShutdownIfEmpty();
        main.Show();
        Log("Main window shown", null);
    }

    private void ShutdownIfEmpty()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (Windows.Count == 0)
            {
                Log("No windows left, shutting down", null);
                Shutdown();
            }
        }, DispatcherPriority.Background);
    }

    private static void Log(string message, Exception? exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break the application.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _api?.Dispose();
        base.OnExit(e);
    }
}
