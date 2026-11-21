using MusicHub.Infrastructure.Security;

namespace MusicHub.Tests.Unit;

/// <summary>
/// Unit tests for the installer-written database credential and for the connection-string
/// resolver that the API and the Worker both use.
///
/// <para>
/// These are the rules that decide which database a MusicHub host connects to, so they are
/// deliberately strict: a missing file may fall back to configuration, but an unreadable file
/// must never do so silently.
/// </para>
/// </summary>
public sealed class DatabaseCredentialTests : IDisposable
{
    private const string SampleConnection =
        "Host=127.0.0.1;Port=5433;Database=musichub;Username=musichub;Password=s3cr3t-Example-Value";

    private readonly string _root;

    public DatabaseCredentialTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "musichub-dbcred-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private Microsoft.AspNetCore.DataProtection.IDataProtectionProvider NewProvider(string root) =>
        DataProtectionSetup.Create(root);

    [Fact]
    public void Write_ThenRead_RoundTripsTheConnectionString()
    {
        var provider = NewProvider(_root);

        var path = DatabaseCredentialFile.Write(_root, SampleConnection, provider);

        Assert.True(File.Exists(path));
        var credential = DatabaseCredentialFile.TryRead(_root, provider);
        Assert.Equal(CredentialFileState.Ok, credential.State);
        Assert.Equal(SampleConnection, credential.ConnectionString);
    }

    [Fact]
    public void WrittenFile_DoesNotContainThePasswordInPlaintext()
    {
        var provider = NewProvider(_root);
        var path = DatabaseCredentialFile.Write(_root, SampleConnection, provider);

        var content = File.ReadAllText(path);

        Assert.DoesNotContain("s3cr3t-Example-Value", content);
        Assert.DoesNotContain("Password=", content);
        Assert.DoesNotContain("Host=", content);
    }

    [Fact]
    public void Reading_WithADifferentKeyring_ReportsUnreadable()
    {
        DatabaseCredentialFile.Write(_root, SampleConnection, NewProvider(_root));

        // A second data root has its own keyring, exactly like a restored data directory that
        // was separated from its keys.
        var otherRoot = Path.Combine(_root, "other");
        Directory.CreateDirectory(otherRoot);
        var otherProvider = NewProvider(otherRoot);

        var credential = DatabaseCredentialFile.TryRead(_root, otherProvider);

        Assert.Equal(CredentialFileState.Unreadable, credential.State);
        Assert.Null(credential.ConnectionString);
    }

    [Fact]
    public void MissingFile_ReportsMissing()
    {
        var credential = DatabaseCredentialFile.TryRead(_root, NewProvider(_root));

        Assert.Equal(CredentialFileState.Missing, credential.State);
    }

    [Fact]
    public void CorruptFile_ReportsUnreadable()
    {
        var provider = NewProvider(_root);
        var path = DatabaseCredentialFile.PathFor(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not-a-protected-payload");

        var credential = DatabaseCredentialFile.TryRead(_root, provider);

        Assert.Equal(CredentialFileState.Unreadable, credential.State);
    }

    [Fact]
    public void Write_ReplacesAnExistingFileAtomically()
    {
        var provider = NewProvider(_root);
        DatabaseCredentialFile.Write(_root, SampleConnection, provider);

        const string replacement = "Host=127.0.0.1;Port=5433;Database=other;Username=musichub;Password=rotated";
        DatabaseCredentialFile.Write(_root, replacement, provider);

        var credential = DatabaseCredentialFile.TryRead(_root, provider);
        Assert.Equal(CredentialFileState.Ok, credential.State);
        Assert.Equal(replacement, credential.ConnectionString);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Resolve_PrefersTheEnvironmentOverride()
    {
        var provider = NewProvider(_root);
        DatabaseCredentialFile.Write(_root, SampleConnection, provider);
        const string overrideValue = "Host=override;Port=6000;Database=other;Username=u;Password=p";

        var previous = Environment.GetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, overrideValue);

            var resolved = ConnectionStringResolver.Resolve(_root, null, provider, allowDevelopmentDefault: false);

            Assert.Equal(overrideValue, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Resolve_UsesTheCredentialFileWhenNoOverrideIsSet()
    {
        var provider = NewProvider(_root);
        DatabaseCredentialFile.Write(_root, SampleConnection, provider);

        var previous = Environment.GetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, null);

            var resolved = ConnectionStringResolver.Resolve(_root, "Host=configured", provider, allowDevelopmentDefault: false);

            Assert.Equal(SampleConnection, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Resolve_FallsBackToConfigurationOnlyWhenTheFileIsMissing()
    {
        var provider = NewProvider(_root);

        var previous = Environment.GetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, null);

            var resolved = ConnectionStringResolver.Resolve(_root, "Host=configured", provider, allowDevelopmentDefault: false);

            Assert.Equal("Host=configured", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Resolve_ThrowsForAnUnreadableFileInsteadOfFallingBack()
    {
        // Write with one keyring, then resolve with another: the file exists but cannot be read.
        DatabaseCredentialFile.Write(_root, SampleConnection, NewProvider(_root));
        var foreignRoot = Path.Combine(_root, "foreign");
        Directory.CreateDirectory(foreignRoot);
        var foreignProvider = NewProvider(foreignRoot);

        var previous = Environment.GetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, null);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ConnectionStringResolver.Resolve(_root, "Host=configured", foreignProvider, allowDevelopmentDefault: true));

            Assert.Contains("cannot be decrypted", exception.Message);
            // The message may name the file but must never carry the secret itself.
            Assert.DoesNotContain("s3cr3t-Example-Value", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Resolve_ThrowsInProductionWhenNothingIsConfigured()
    {
        var provider = NewProvider(_root);
        var previous = Environment.GetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, null);

            Assert.Throws<InvalidOperationException>(() =>
                ConnectionStringResolver.Resolve(_root, null, provider, allowDevelopmentDefault: false));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolveDataRoot_PrefersTheEnvironmentVariable()
    {
        var previous = Environment.GetEnvironmentVariable(ConnectionStringResolver.DataRootEnvironmentVariable);
        try
        {
            var fromEnvironment = Path.Combine(_root, "env-root");
            Environment.SetEnvironmentVariable(ConnectionStringResolver.DataRootEnvironmentVariable, fromEnvironment);

            var resolved = ConnectionStringResolver.ResolveDataRoot(Path.Combine(_root, "configured"), _root);

            Assert.Equal(Path.GetFullPath(fromEnvironment), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.DataRootEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void DataProtectionSetup_WritesKeysUnderTheDataRoot()
    {
        // Keys are generated lazily on first use, so protect something before looking for them.
        var provider = NewProvider(_root);
        DatabaseCredentialFile.Write(_root, SampleConnection, provider);

        var keyPath = DataProtectionSetup.KeyPathFor(_root);
        Assert.True(Directory.Exists(keyPath));
        Assert.NotEmpty(Directory.GetFiles(keyPath, "*.xml"));
    }

    [Fact]
    public void NamedCredentialFiles_AreIndependent()
    {
        var provider = NewProvider(_root);
        const string adminConnection = "Host=127.0.0.1;Port=5433;Database=postgres;Username=postgres;Password=admin";

        DatabaseCredentialFile.Write(_root, SampleConnection, provider);
        DatabaseCredentialFile.Write(_root, DatabaseCredentialFile.AdminFileName, adminConnection, provider);

        Assert.Equal(
            SampleConnection,
            DatabaseCredentialFile.TryRead(_root, provider).ConnectionString);
        Assert.Equal(
            adminConnection,
            DatabaseCredentialFile.TryRead(_root, DatabaseCredentialFile.AdminFileName, provider).ConnectionString);

        // The application credential must not exist until it is written, so a half-finished
        // install cannot be mistaken for a working one.
        var otherRoot = Path.Combine(_root, "empty");
        Directory.CreateDirectory(otherRoot);
        Assert.Equal(
            CredentialFileState.Missing,
            DatabaseCredentialFile.TryRead(otherRoot, DatabaseCredentialFile.AdminFileName, NewProvider(otherRoot)).State);
    }

    [Theory]
    [InlineData("../escape.protected")]
    [InlineData("sub/dir/credential.protected")]
    [InlineData("..\\escape.protected")]
    public void Write_RejectsAFileNameThatWouldEscapeTheKeysDirectory(string fileName)
    {
        var provider = NewProvider(_root);

        var exception = Assert.Throws<ArgumentException>(() =>
            DatabaseCredentialFile.Write(_root, fileName, SampleConnection, provider));

        Assert.DoesNotContain("s3cr3t-Example-Value", exception.Message);
    }
}
