using System.Text.RegularExpressions;

namespace MusicHub.Tests.Unit;

/// <summary>
/// The install path must work on any host, so it may not contain one machine's addressing or one
/// fixed listening port. <c>deploy/scripts/network.ps1</c> discovers the LAN address and asks the
/// operator for a port; <c>deploy/docker/install.ps1</c> does the same for the container path.
///
/// <para>
/// These are source-level assertions, not a PowerShell execution. The behaviour of the module
/// itself was exercised on the authoring host (address detection, free-port search, endpoint
/// persistence and reuse); these tests stop a literal from being reintroduced into the scripts that
/// call it, which is the regression that would silently break the next machine.
/// </para>
/// </summary>
public sealed class InstallEndpointTests
{
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MusicHub.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("MusicHub.slnx was not found above the test output directory.");
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(relativePath).ToArray());
        Assert.True(File.Exists(path), $"{path} no longer exists.");
        return File.ReadAllText(path);
    }

    /// <summary>Executable (non-comment, non-blank) lines only.</summary>
    private static List<string> ExecutableLines(string text) =>
        text.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .ToList();

    private static readonly Regex PrivateAddressLiteral = new(
        @"\b(?:10\.\d{1,3}|192\.168|172\.(?:1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}\b",
        RegexOptions.Compiled);

    [Fact]
    public void The_network_module_provides_discovery_port_selection_and_persistence()
    {
        var text = ReadRepoFile("deploy", "scripts", "network.ps1");

        foreach (var function in new[]
        {
            "Get-MusicHubLanAddress",
            "Get-MusicHubPrimaryLanAddress",
            "Test-MusicHubPortAvailable",
            "Find-MusicHubFreePort",
            "Select-MusicHubListenPort",
            "Select-MusicHubLanAddress",
            "Resolve-MusicHubEndpoint",
            "Import-MusicHubEndpoint",
            "Save-MusicHubEndpoint",
            "Write-MusicHubAccessSummary"
        })
        {
            Assert.Contains($"function {function}", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_network_module_contains_no_hardcoded_private_address()
    {
        var text = ReadRepoFile("deploy", "scripts", "network.ps1");

        // The module is the thing that discovers the address. A literal in it would mean the
        // discovery is bypassed and the install is pinned to one machine.
        var literal = PrivateAddressLiteral.Match(text);
        Assert.False(literal.Success, $"network.ps1 contains a hard-coded address: '{literal.Value}'");
    }

    [Fact]
    public void Both_install_paths_resolve_the_endpoint_instead_of_hardcoding_it()
    {
        var windowsStart = ReadRepoFile("deploy", "scripts", "start-dev.ps1");
        var windowsInstall = ReadRepoFile("deploy", "scripts", "install-server.ps1");
        var dockerInstall = ReadRepoFile("deploy", "docker", "install.ps1");

        foreach (var (name, text) in new[]
        {
            ("start-dev.ps1", windowsStart),
            ("install-server.ps1", windowsInstall),
            ("deploy/docker/install.ps1", dockerInstall)
        })
        {
            Assert.Contains("Resolve-MusicHubEndpoint", text, StringComparison.Ordinal);
        }

        // The Docker path must record the operator's answer in .env so the compose overlay has no
        // literal and no fixed default.
        Assert.Contains("MUSICHUB_LAN_ADDRESS", dockerInstall, StringComparison.Ordinal);
        Assert.Contains("MUSICHUB_LAN_PORT", dockerInstall, StringComparison.Ordinal);
    }

    [Fact]
    public void The_docker_install_generates_secrets_without_ever_printing_them()
    {
        var text = ReadRepoFile("deploy", "docker", "install.ps1");

        // Secrets are written straight into .env by the setter, never echoed.
        Assert.Contains("Set-MusicHubEnvValue", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $secret", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Write-MusicHubInfo $secret", text, StringComparison.OrdinalIgnoreCase);

        // Only placeholders may be regenerated: an existing real value must survive a re-run.
        Assert.Contains("CHANGE_ME", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_record_is_written_under_the_ignored_data_root()
    {
        var text = ReadRepoFile("deploy", "scripts", "network.ps1");

        Assert.Contains("install.endpoint.json", text, StringComparison.Ordinal);
        Assert.Contains("'storage'", text, StringComparison.Ordinal);

        // It is JSON, not PowerShell: reading a data file must never execute anything.
        Assert.Contains("ConvertTo-Json", text, StringComparison.Ordinal);
        Assert.Contains("ConvertFrom-Json", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-Expression", text, StringComparison.Ordinal);
        Assert.DoesNotContain("iex ", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_installer_script_contains_a_private_address_literal()
    {
        var offenders = new List<string>();
        foreach (var relative in new[]
        {
            new[] { "deploy", "scripts", "network.ps1" },
            new[] { "deploy", "scripts", "start-dev.ps1" },
            new[] { "deploy", "scripts", "install-server.ps1" },
            new[] { "deploy", "docker", "install.ps1" },
            new[] { "deploy", "docker", "docker-compose.lan.yml" }
        })
        {
            var text = ReadRepoFile(relative);
            foreach (var line in ExecutableLines(text))
            {
                var match = PrivateAddressLiteral.Match(line);
                if (match.Success)
                {
                    offenders.Add($"{string.Join('/', relative)}: '{match.Value}'");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Installer files must discover the LAN address, not embed one: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// A default install must not leave Caddy requesting a certificate for a domain the operator
    /// does not own. The template ships <c>music.example.com</c>; the installer has to recognise that
    /// value as unconfigured (the same way it already treats the template public URL) and move the
    /// edge to <c>localhost</c> with a warning. The LAN path bypasses Caddy, so a LAN-only install
    /// keeps working either way - this only prevents a silent, endless ACME failure.
    /// </summary>
    [Fact]
    public void The_docker_install_does_not_leave_the_template_hostname_on_the_tls_edge()
    {
        var text = ReadRepoFile("deploy", "docker", "install.ps1");
        var code = string.Join('\n', ExecutableLines(text));

        // The template value must be read from .env and compared against, not merely mentioned.
        Assert.Matches(
            new Regex(@"Get-MusicHubEnvValue[^\n]*MUSICHUB_PUBLIC_HOSTNAME"),
            code);
        Assert.Matches(
            new Regex(@"\$currentHostname\s+-eq\s+'music\.example\.com'"),
            code);

        // The corrective action must be a real .env write to localhost, not just a message.
        Assert.Matches(
            new Regex(@"Set-MusicHubEnvValue[^\n]*MUSICHUB_PUBLIC_HOSTNAME[^\n]*'localhost'"),
            code);

        // An explicit -PublicHostname must still win: the guard is conditional on it being absent.
        Assert.Matches(new Regex(@"-not\s+\$PublicHostname"), code);

        // The operator has to be told, because the public HTTPS edge is then not configured.
        Assert.Contains("Write-MusicHubWarning", text, StringComparison.Ordinal);
    }
}
