using System.IO;
using System.Text.Json;

namespace MusicHub.Player.Services;

public sealed class PlayerPreferences
{
    public int Volume { get; set; } = 80;
    public bool Shuffle { get; set; }
    public int RepeatMode { get; set; }
    public bool RememberSession { get; set; } = true;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicHub");

    private static string FilePath => Path.Combine(DirectoryPath, "player-settings.json");

    public static PlayerPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new PlayerPreferences();
            return JsonSerializer.Deserialize<PlayerPreferences>(File.ReadAllText(FilePath), Options) ?? new PlayerPreferences();
        }
        catch
        {
            return new PlayerPreferences();
        }
    }

    public static void Save(PlayerPreferences preferences)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(preferences, Options));
        }
        catch
        {
            // Preferences are not critical.
        }
    }
}
