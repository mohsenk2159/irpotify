namespace MusicHub.Infrastructure.Storage;

/// <summary>One safety check on a proposed library folder.</summary>
/// <param name="Id">Stable identifier, e.g. <c>path.not.system</c>. Clients group on it, so it is part of the contract.</param>
/// <param name="Ok">Whether the candidate passed.</param>
/// <param name="Message">Why it failed, in operator language. Null when it passed.</param>
public sealed record StorageCheck(string Id, bool Ok, string? Message);

/// <summary>
/// Decides whether a folder may become the library root.
/// </summary>
/// <remarks>
/// <para>
/// The checks run in order and the function returns as soon as one fails. That ordering is the
/// safety property, not a style choice: creating a probe file inside <c>C:\Windows</c> is precisely
/// the action <c>path.not.system</c> exists to prevent, so a rejected path is never created, probed
/// or otherwise touched.
/// </para>
/// <para>
/// This is server-side authorization-adjacent validation. A client that skips it only loses a
/// helpful message; the relocation service runs the same validator again before it copies anything.
/// </para>
/// </remarks>
public static class StorageLocationValidator
{
    public static IReadOnlyList<StorageCheck> Validate(
        string? candidate,
        string currentLibraryRoot,
        string dataRoot,
        bool requireEmpty = true)
    {
        var checks = new List<StorageCheck>();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            checks.Add(new StorageCheck("path.present", false, "A folder path is required."));
            return checks;
        }
        checks.Add(new StorageCheck("path.present", true, null));

        if (!Path.IsPathFullyQualified(candidate))
        {
            checks.Add(new StorageCheck("path.absolute", false,
                @"Use a full path such as D:\MusicHub\library."));
            return checks;
        }
        checks.Add(new StorageCheck("path.absolute", true, null));

        string full;
        try
        {
            full = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            checks.Add(new StorageCheck("path.valid", false, "The path is not valid: " + ex.Message));
            return checks;
        }
        checks.Add(new StorageCheck("path.valid", true, null));

        checks.Add(new StorageCheck("path.no.traversal", !candidate.Contains("..", StringComparison.Ordinal),
            "The path must not contain '..'."));

        checks.Add(new StorageCheck("path.is.directory", !File.Exists(full),
            "A file already exists at this path."));

        var isDriveRoot = string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase);
        checks.Add(new StorageCheck("path.not.drive.root", !isDriveRoot,
            @"Choose a folder inside the drive, not the drive itself (D:\ is refused, D:\MusicHub is fine)."));

        var systemDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.System)
        };
        var inSystemDirectory = systemDirectories
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Any(x => IsUnder(full, x));
        checks.Add(new StorageCheck("path.not.system", !inSystemDirectory,
            "The Windows, System and Program Files folders cannot hold the library."));

        var underApplication = IsUnder(full, AppContext.BaseDirectory);
        checks.Add(new StorageCheck("path.outside.application", !underApplication,
            "Keep the library outside the application folder so an upgrade cannot overwrite it."));

        checks.Add(new StorageCheck("path.not.current",
            !string.Equals(full, Path.GetFullPath(currentLibraryRoot), StringComparison.OrdinalIgnoreCase),
            "This is already the current library location."));

        checks.Add(new StorageCheck("path.not.data.root",
            !string.Equals(full, Path.GetFullPath(dataRoot), StringComparison.OrdinalIgnoreCase),
            "This is the server data folder. It holds the keys and the database credential, so it cannot become the library."));

        var keysRoot = Path.Combine(Path.GetFullPath(dataRoot), "keys");
        checks.Add(new StorageCheck("path.not.keys",
            !string.Equals(full, keysRoot, StringComparison.OrdinalIgnoreCase) && !IsUnder(full, keysRoot),
            "The keys folder holds credentials and cannot be used for music."));

        // From here on the path is safe to touch.
        if (checks.Exists(x => !x.Ok)) return checks;

        var existed = Directory.Exists(full);
        var created = false;
        try
        {
            if (!existed)
            {
                Directory.CreateDirectory(full);
                created = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            checks.Add(new StorageCheck("path.creatable", false, "The folder could not be created: " + ex.Message));
            return checks;
        }
        checks.Add(new StorageCheck("path.creatable", true, null));

        string? writeError = null;
        try
        {
            var probe = Path.Combine(full, ".musichub-write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            writeError = ex.Message;
        }
        checks.Add(new StorageCheck("path.writable", writeError is null,
            writeError is null ? null : "The folder is not writable: " + writeError));

        if (requireEmpty)
        {
            var empty = Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full)
                .Any(name => !Path.GetFileName(name).StartsWith(".musichub-", StringComparison.Ordinal));
            checks.Add(new StorageCheck("path.empty", empty,
                "The folder already contains files. Choose an empty folder so an existing library cannot be overwritten."));
        }

        if (created)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(full).Any()) Directory.Delete(full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leaving an empty folder behind is harmless; failing the check would not be.
            }
        }

        return checks;
    }

    internal static bool IsUnder(string candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent)) return false;
        var prefix = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
