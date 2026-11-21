using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MusicHub.Player.Models;

namespace MusicHub.Player.Services;

/// <summary>
/// Persists the signed-in session to disk encrypted with Windows DPAPI (per-user scope),
/// so tokens are never stored in plain text. Only the server URL and the email are kept
/// in clear text inside the encrypted blob.
/// </summary>
public static class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicHub");

    private static string FilePath => Path.Combine(DirectoryPath, "session.dat");

    public static SavedSession? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var protectedBytes = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<SavedSession>(json, JsonOptions);
        }
        catch
        {
            // A corrupted or foreign-user blob must never block startup.
            return null;
        }
    }

    public static void Save(SavedSession session)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(session, JsonOptions);
        var plain = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, protectedBytes);
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch
        {
            // Ignore: the caller still clears the in-memory session.
        }
    }

    private static byte[] Entropy => Encoding.UTF8.GetBytes("MusicHub.Player.Session.v1");
}
