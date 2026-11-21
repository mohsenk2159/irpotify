using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace MusicHub.Infrastructure.Security;

/// <summary>Outcome of reading the credential file.</summary>
public enum CredentialFileState
{
    /// <summary>No credential file exists; the caller falls back to another source.</summary>
    Missing = 0,

    /// <summary>A credential file exists and decrypted successfully.</summary>
    Ok = 1,

    /// <summary>
    /// A credential file exists but cannot be decrypted: a different keyring, a different
    /// account, or a damaged file. This must be treated as an error, never as "absent",
    /// because silently falling back would connect with a different password.
    /// </summary>
    Unreadable = 2
}

public readonly record struct DatabaseCredential(string? ConnectionString, CredentialFileState State);

/// <summary>
/// Stores the PostgreSQL credential that the installer generates for the application role.
///
/// <para>
/// The connection string is protected with the application's data-protection keyring, which on
/// Windows is itself DPAPI-encrypted for the running account, and written to a single file under
/// the data root. The file is created with restrictive permissions, written atomically, and the
/// plaintext password is never placed on a command line, in a log, or in an exception message.
/// </para>
/// <para>
/// The API and the Worker must resolve their connection string through
/// <see cref="ConnectionStringResolver"/> so both read the same file with the same keyring.
/// </para>
/// </summary>
public static class DatabaseCredentialFile
{
    public const string DirectoryName = "keys";
    public const string FileName = "db-credential.protected";

    /// <summary>
    /// Data-protection purpose string. Changing it intentionally invalidates every credential
    /// file written by an earlier version, so it carries an explicit version suffix.
    /// </summary>
    public const string ProtectorPurpose = "MusicHub.DatabaseCredential.v1";

    /// <summary>
    /// Credential file holding the administrator (superuser) connection string that a fresh
    /// install uses once, to create the application role. It is never read by the API or Worker.
    /// </summary>
    public const string AdminFileName = "db-admin-credential.protected";

    /// <summary>
    /// Data-protection application name shared by the API, the Worker and the installer. All
    /// three must agree, otherwise they derive different keys from the same keyring and the
    /// credential file becomes unreadable.
    /// </summary>
    public const string DataProtectionApplicationName = "MusicHub";

    public static string PathFor(string dataRoot) =>
        System.IO.Path.Combine(dataRoot, DirectoryName, FileName);

    /// <summary>Path of a named credential file (see <see cref="FileName"/> / <see cref="AdminFileName"/>).</summary>
    public static string PathFor(string dataRoot, string fileName) =>
        System.IO.Path.Combine(dataRoot, DirectoryName, fileName);

    public static IDataProtector CreateProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.CreateProtector(ProtectorPurpose);
    }

    /// <summary>
    /// Protect and persist a connection string. Returns the path written.
    /// </summary>
    public static string Write(string dataRoot, string connectionString, IDataProtectionProvider provider) =>
        Write(dataRoot, FileName, connectionString, provider);

    /// <summary>
    /// Protect and persist a connection string under a named credential file.
    /// </summary>
    public static string Write(
        string dataRoot,
        string fileName,
        string connectionString,
        IDataProtectionProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Only a bare file name is accepted so a caller cannot escape the keys directory.
        if (fileName.IndexOfAny(['/', '\\']) >= 0 || fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("The credential file name must be a bare file name.", nameof(fileName));
        }

        var path = PathFor(dataRoot, fileName);
        var directory = System.IO.Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        RestrictDirectory(directory);

        var ciphertext = CreateProtector(provider).Protect(connectionString);
        var bytes = System.Text.Encoding.UTF8.GetBytes(ciphertext);

        // Write to a sibling temp file and move it into place so a crash can never leave a
        // half-written credential behind, which would look like an unreadable file at startup.
        var temp = path + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) + ".tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temp, options))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            RestrictFile(temp);
            File.Move(temp, path, overwrite: true);
            RestrictFile(path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (File.Exists(temp)) TryDelete(temp);
        }

        return path;
    }

    /// <summary>
    /// Read and unprotect the connection string. Never throws for a missing or damaged file;
    /// reports the state so the caller can decide (see <see cref="CredentialFileState"/>).
    /// </summary>
    public static DatabaseCredential TryRead(string dataRoot, IDataProtectionProvider provider) =>
        TryRead(dataRoot, FileName, provider);

    /// <summary>
    /// Read and unprotect a named credential file. Never throws for a missing or damaged file;
    /// reports the state so the caller can decide (see <see cref="CredentialFileState"/>).
    /// </summary>
    public static DatabaseCredential TryRead(
        string dataRoot,
        string fileName,
        IDataProtectionProvider provider)
    {
        if (string.IsNullOrWhiteSpace(dataRoot)) return new DatabaseCredential(null, CredentialFileState.Missing);

        var path = PathFor(dataRoot, fileName);

        // Do not decide existence with File.Exists. It returns false both when the file is absent
        // and when the caller is merely denied access, so a permission problem (the supported
        // Windows-service deployment ships a file ACL that grants only the installing account)
        // looked identical to "not installed" and the resolver silently downgraded to
        // ConnectionStrings:Default or the development default (B-040). Attempting the read and
        // classifying the exception keeps the two cases apart: only a genuinely absent file may
        // report Missing, and anything the caller cannot read is Unreadable.
        string ciphertext;
        try
        {
            ciphertext = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return new DatabaseCredential(null, CredentialFileState.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            // .NET reports a file under an inaccessible parent directory as
            // DirectoryNotFoundException on Windows, so this exception alone cannot tell
            // "never installed" from "installed, but this account cannot reach it". Ask the
            // directory itself and fail closed whenever the answer is not a clean "absent".
            return new DatabaseCredential(null, IsCredentialFileAbsent(dataRoot, fileName)
                ? CredentialFileState.Missing
                : CredentialFileState.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return new DatabaseCredential(null, CredentialFileState.Unreadable);
        }
        catch (System.Security.SecurityException)
        {
            return new DatabaseCredential(null, CredentialFileState.Unreadable);
        }
        catch (IOException)
        {
            return new DatabaseCredential(null, CredentialFileState.Unreadable);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(ciphertext)) return new DatabaseCredential(null, CredentialFileState.Unreadable);

            var protector = CreateProtector(provider);
            var plaintext = protector.Unprotect(ciphertext);
            if (string.IsNullOrWhiteSpace(plaintext)) return new DatabaseCredential(null, CredentialFileState.Unreadable);
            return new DatabaseCredential(plaintext, CredentialFileState.Ok);
        }
        catch (CryptographicException)
        {
            // Wrong keyring, wrong account, or tampered content. Deliberately not surfaced with
            // the underlying message: it can name key files and would add no diagnostic value.
            return new DatabaseCredential(null, CredentialFileState.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return new DatabaseCredential(null, CredentialFileState.Unreadable);
        }
    }

    /// <summary>
    /// Decide whether the credential file is genuinely absent, rather than merely unreachable.
    ///
    /// <para>
    /// Only this method may answer "absent". Every ambiguous answer — the directory cannot be
    /// enumerated, the share is unavailable, the name is taken by something that is not a
    /// readable file — resolves to <c>false</c> so the caller reports <c>Unreadable</c> and the
    /// resolver fails loudly instead of connecting with a different password (B-040).
    /// </para>
    /// </summary>
    private static bool IsCredentialFileAbsent(string dataRoot, string fileName)
    {
        var directory = System.IO.Path.Combine(dataRoot, DirectoryName);
        try
        {
            // Enumerating instead of Directory.Exists/File.Exists: both of those return false on
            // an access denial, which is exactly the conflation being fixed. Enumeration reports
            // the denial as an exception we can see.
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (string.Equals(System.IO.Path.GetFileName(entry), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    // It is there; the read failed for another reason, so fail closed.
                    return false;
                }
            }

            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private static void RestrictDirectory(string directory)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the parent data root is already expected to be restricted.
        }
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            return;
        }

        // Windows: remove inherited access and grant only the current account. The content is
        // already DPAPI-encrypted for this account, so this is defence in depth, not the control.
        var account = $"{Environment.UserDomainName}\\{Environment.UserName}";
        if (!TryRunIcacls(path, "/inheritance:r", "/grant:r", $"{account}:(R,W)"))
        {
            TryRunIcacls(path, "/inheritance:r", "/grant:r", $"{Environment.UserName}:(R,W)");
        }
    }

    private static bool TryRunIcacls(string path, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("icacls.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.ArgumentList.Add(path);
            foreach (var argument in arguments) info.ArgumentList.Add(argument);

            using var process = Process.Start(info);
            if (process is null) return false;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
