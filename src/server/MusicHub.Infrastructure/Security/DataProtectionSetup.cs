using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace MusicHub.Infrastructure.Security;

/// <summary>
/// Builds the data-protection provider used to protect installer-written secrets (the database
/// credential, the Telegram bot token, pending Telegram files).
///
/// <para>
/// The provider is created eagerly, before the dependency-injection container resolves anything,
/// because the connection string itself may come out of the credential file. The same instance is
/// then registered as a singleton so every consumer shares one keyring and one key cache instead
/// of opening the key directory twice.
/// </para>
/// <para>
/// Keys persist under <c>&lt;data root&gt;/keys/dataprotection</c>. On Windows they are additionally
/// DPAPI-encrypted for the running account, so copying the data root to another machine or account
/// does not expose them.
/// </para>
/// </summary>
public static class DataProtectionSetup
{
    /// <summary>Key directory relative to the data root.</summary>
    public static string KeyPathFor(string dataRoot) =>
        Path.Combine(dataRoot, "keys", "dataprotection");

    /// <summary>
    /// Create the provider for a data root. Safe to call from the API, the Worker and the
    /// installer; all three must pass the same data root and application name.
    /// </summary>
    public static IDataProtectionProvider Create(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        var keyPath = KeyPathFor(dataRoot);
        Directory.CreateDirectory(keyPath);

        // Built through a minimal container because the standalone DataProtectionProvider
        // factory lives in a separate package; this path uses only references the server and
        // worker projects already carry.
        var services = new ServiceCollection();
        var dataProtection = services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
            .SetApplicationName(DatabaseCredentialFile.DataProtectionApplicationName);
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }
}
