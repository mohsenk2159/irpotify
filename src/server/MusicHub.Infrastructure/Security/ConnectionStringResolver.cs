using Microsoft.AspNetCore.DataProtection;

namespace MusicHub.Infrastructure.Security;

/// <summary>
/// Resolves the storage data root and the PostgreSQL connection string for a MusicHub host.
///
/// <para>
/// Precedence for the data root: <c>MUSICHUB_DATA_ROOT</c>, then configuration, then the
/// environment-specific fallback the caller supplies. The API and the Worker must resolve the
/// same root because the database credential file lives under it.
/// </para>
/// <para>
/// Precedence for the connection string: <c>MUSICHUB_CONNECTION_STRING</c> (an operator override
/// used by containers), then the installer-written credential file, then configuration
/// (<c>ConnectionStrings:Default</c>), then the development default. An override supplied as an
/// environment variable is honoured even when a credential file exists, so a deployment can point
/// at an externally managed database without editing the file.
/// </para>
/// </summary>
public static class ConnectionStringResolver
{
    public const string EnvironmentVariable = "MUSICHUB_CONNECTION_STRING";
    public const string DataRootEnvironmentVariable = "MUSICHUB_DATA_ROOT";

    /// <summary>
    /// Development-only default. It is used when nothing is configured at all, which cannot
    /// happen after a normal install because the installer writes a credential file.
    /// </summary>
    public const string DevelopmentDefault =
        "Host=127.0.0.1;Port=5433;Database=musichub;Username=musichub;Password=musichub";

    public static string ResolveDataRoot(string configuredRoot, string fallbackRoot)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return Path.GetFullPath(fromEnvironment);
        if (!string.IsNullOrWhiteSpace(configuredRoot)) return Path.GetFullPath(configuredRoot);
        return Path.GetFullPath(fallbackRoot);
    }

    /// <summary>
    /// Resolve the connection string.
    /// </summary>
    /// <param name="dataRoot">Data root that may contain the credential file.</param>
    /// <param name="configurationValue">Value of <c>ConnectionStrings:Default</c>, if any.</param>
    /// <param name="dataProtection">
    /// The host's data-protection provider. It must be the same provider (same keyring and
    /// application name) the installer used, or an existing credential file is unreadable.
    /// </param>
    /// <param name="allowDevelopmentDefault">
    /// Whether the development default may be used when no credential file exists. Hosts running
    /// in Production pass <c>false</c> so a missing install step fails loudly.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A credential file exists but cannot be decrypted, or no source is configured.
    /// </exception>
    /// <summary>
    /// The only environment name that may fall back to <see cref="DevelopmentDefault"/>.
    ///
    /// <para>
    /// This exists so the API and the Worker cannot disagree. They did: the API passed
    /// <c>IsDevelopment()</c> while the Worker hard-coded <c>true</c>, so a Production Worker with
    /// no credential file connected silently to the development default (B-041). Both hosts now
    /// derive the flag from this single function.
    /// </para>
    /// </summary>
    public static bool AllowsDevelopmentDefault(string? environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve the connection string for a host, applying the shared environment policy.
    /// </summary>
    /// <param name="environmentName">
    /// The host's environment name (<c>builder.Environment.EnvironmentName</c>).
    /// </param>
    public static string ResolveForHost(
        string? environmentName,
        string dataRoot,
        string? configurationValue,
        IDataProtectionProvider dataProtection) =>
        Resolve(
            dataRoot,
            configurationValue,
            dataProtection,
            AllowsDevelopmentDefault(environmentName));

    public static string Resolve(
        string dataRoot,
        string? configurationValue,
        IDataProtectionProvider dataProtection,
        bool allowDevelopmentDefault)
    {
        var overrideValue = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideValue)) return overrideValue;

        var credential = DatabaseCredentialFile.TryRead(dataRoot, dataProtection);
        switch (credential.State)
        {
            case CredentialFileState.Ok:
                return credential.ConnectionString!;

            case CredentialFileState.Unreadable:
                // Never fall back silently: the plaintext is unavailable and any other source
                // would connect with a different password than the one PostgreSQL was given,
                // producing a confusing authentication failure far from the real cause.
                throw new InvalidOperationException(
                    $"The database credential file at '{DatabaseCredentialFile.PathFor(dataRoot)}' exists but cannot be decrypted. " +
                    "It is protected for the account and data-protection keyring that created it, so a different account, a restored " +
                    "data root without its keyring, or a damaged file produces this error. Re-run the installer to write a new " +
                    $"credential file, or set {EnvironmentVariable} to supply the connection string explicitly.");

            default:
                if (!string.IsNullOrWhiteSpace(configurationValue)) return configurationValue;

                if (allowDevelopmentDefault) return DevelopmentDefault;

                throw new InvalidOperationException(
                    "No database connection string is available. The installer normally writes " +
                    $"'{DatabaseCredentialFile.PathFor(dataRoot)}'; set {EnvironmentVariable} or " +
                    "ConnectionStrings:Default when running without an installer.");
        }
    }
}
