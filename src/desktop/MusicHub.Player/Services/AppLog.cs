using System.IO;

namespace MusicHub.Player.Services;

/// <summary>
/// Minimal append-only diagnostics log. Player diagnostics live next to the encrypted
/// session under %APPDATA%\MusicHub so support can read them without a debugger.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicHub", "player.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath,
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}" +
                    (exception is null ? string.Empty : Environment.NewLine + exception) +
                    Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never break playback.
        }
    }
}
