using System.Globalization;

namespace MusicHub.Tests.Unit;

/// <summary>
/// Structural tests for <c>deploy/docker/docker-compose.yml</c>.
///
/// <para>
/// B-060: <c>api</c> and <c>worker</c> set <c>pids_limit: 512</c> together with a
/// <c>deploy.resources.limits</c> block that named only <c>cpus</c> and <c>memory</c>. Compose
/// rejects that combination outright - "can't set distinct values on 'pids_limit' and
/// 'deploy.resources.limits.pids'" - so <c>docker compose up</c> failed before it created a single
/// container. The file had only ever been read, never validated, which is why the status stayed
/// <c>IMPLEMENTED_NOT_TESTED</c>.
/// </para>
/// <para>
/// These tests parse the file directly instead of shelling out to <c>docker compose</c>, so they
/// run everywhere and stay deterministic. The behaviour they guard was confirmed against Compose
/// v5.7.0: <c>pids_limit</c> plus <c>deploy.resources.limits</c> validates only when
/// <c>limits.pids</c> is present and equal.
/// </para>
/// </summary>
public sealed class ComposeTopologyTests
{
    private const string ComposeRelativePath = "deploy/docker/docker-compose.yml";

    private sealed record Entry(int Indent, string Key, string? Value);

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

    private static int IndentOf(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ') count++;
        return count;
    }

    /// <summary>
    /// Flatten the YAML into (indent, key, value) entries. Sequence items are kept as key "-" so a
    /// value such as <c>- no-new-privileges:true</c> cannot be mistaken for a mapping key.
    /// </summary>
    private static List<Entry> ParseEntries()
    {
        var path = Path.Combine(FindRepoRoot(), ComposeRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{path} no longer exists.");

        var entries = new List<Entry>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var indent = IndentOf(raw);

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-")
            {
                entries.Add(new Entry(indent, "-", trimmed.Length > 1 ? trimmed[1..].Trim() : null));
                continue;
            }

            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
            {
                entries.Add(new Entry(indent, trimmed, null));
                continue;
            }

            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            entries.Add(new Entry(indent, key, value.Length == 0 ? null : value));
        }

        return entries;
    }

    /// <summary>Entries that sit directly on the service, not inside a nested block.</summary>
    private static List<Entry> TopLevelEntries(IReadOnlyList<Entry> block)
    {
        if (block.Count == 0) return [];
        var minimum = block.Min(entry => entry.Indent);
        return block.Where(entry => entry.Indent == minimum).ToList();
    }

    /// <summary>True when <paramref name="index"/> sits inside the given ancestor chain.</summary>
    /// <param name="ancestors">Outermost first, e.g. deploy, resources, limits.</param>
    private static bool IsUnder(IReadOnlyList<Entry> block, int index, params string[] ancestors)
    {
        if (ancestors.Length == 0) return true;

        var remaining = ancestors.Length - 1;
        var currentIndent = block[index].Indent;

        for (var j = index - 1; j >= 0 && remaining >= 0; j--)
        {
            if (block[j].Indent < currentIndent && block[j].Key == ancestors[remaining])
            {
                remaining--;
                currentIndent = block[j].Indent;
            }
        }

        return remaining < 0;
    }

    private static Dictionary<string, List<Entry>> Services()
    {
        var entries = ParseEntries();

        var servicesHeader = entries.FindIndex(e => e.Indent == 0 && e.Key == "services");
        Assert.True(servicesHeader >= 0, "The compose file has no top-level services: section.");

        var result = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);

        for (var i = servicesHeader + 1; i < entries.Count; i++)
        {
            if (entries[i].Indent == 0) break;                 // networks:, volumes:, ...
            if (entries[i].Indent != 2) continue;              // only direct children are services
            if (entries[i].Key == "-") continue;

            var name = entries[i].Key;
            var block = new List<Entry>();

            var j = i + 1;
            for (; j < entries.Count && entries[j].Indent > 2; j++) block.Add(entries[j]);

            result[name] = block;
            i = j - 1;
        }

        return result;
    }

    private static long? ValueOf(IReadOnlyList<Entry> block, string key, params string[] ancestors)
    {
        for (var i = 0; i < block.Count; i++)
        {
            if (block[i].Key != key) continue;
            if (ancestors.Length > 0 && !IsUnder(block, i, ancestors)) continue;
            if (block[i].Value is not null &&
                long.TryParse(block[i].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    [Fact]
    public void The_compose_file_declares_the_four_expected_services()
    {
        var services = Services();

        Assert.Equal(new[] { "api", "caddy", "postgres", "worker" }, services.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// The exact B-060 regression: a service may not set <c>pids_limit</c> alongside
    /// <c>deploy.resources.limits</c> unless <c>limits.pids</c> is also stated.
    /// </summary>
    [Fact]
    public void No_service_sets_pids_limit_without_matching_deploy_limits_pids()
    {
        var offenders = new List<string>();

        foreach (var (name, block) in Services())
        {
            var pidsLimit = ValueOf(block, "pids_limit");
            if (pidsLimit is null) continue;

            var limits = block.Where((_, i) => IsUnder(block, i, "deploy", "resources", "limits")).ToList();
            if (limits.Count == 0) continue;   // no deploy block: pids_limit alone is valid

            var limitsPids = ValueOf(block, "pids", "deploy", "resources", "limits");
            if (limitsPids is null)
            {
                offenders.Add($"{name}: pids_limit={pidsLimit} but deploy.resources.limits has no pids");
            }
            else if (limitsPids != pidsLimit)
            {
                offenders.Add($"{name}: pids_limit={pidsLimit} contradicts limits.pids={limitsPids}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Compose rejects a service that sets pids_limit with deploy.resources.limits and no " +
            "matching limits.pids (B-060): " + string.Join("; ", offenders));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("worker")]
    public void The_process_bound_is_512_on_both_application_services(string service)
    {
        // ffmpeg fan-out is bounded here; dropping it silently would remove a real limit.
        var block = Services()[service];

        Assert.Equal(512, ValueOf(block, "pids_limit"));
        Assert.Equal(512, ValueOf(block, "pids", "deploy", "resources", "limits"));
    }

    /// <summary>
    /// External exposure. B-021's forwarded-header work only holds if the API is genuinely
    /// unreachable except through Caddy, so this is asserted structurally rather than assumed.
    /// </summary>
    [Fact]
    public void Only_caddy_publishes_ports_on_the_host()
    {
        var services = Services();

        var publishers = services
            .Where(pair => TopLevelEntries(pair.Value).Any(entry => entry.Key == "ports"))
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "caddy" }, publishers);

        // The database must never be reachable from the host or the internet.
        Assert.DoesNotContain(TopLevelEntries(services["postgres"]), entry => entry.Key is "ports" or "expose");

        // The worker is internal work only.
        Assert.DoesNotContain(TopLevelEntries(services["worker"]), entry => entry.Key is "ports" or "expose");

        // The API is reachable on the edge network only, never published.
        Assert.DoesNotContain(TopLevelEntries(services["api"]), entry => entry.Key == "ports");
        Assert.Contains(
            services["api"],
            entry => entry.Key == "-" && entry.Value is "5080" or "\"5080\"");
    }

    /// <summary>
    /// Every required variable must fail loudly when it is missing, rather than interpolating to an
    /// empty string. The <c>:?</c> form is what makes a missing <c>.env</c> stop the deployment.
    /// </summary>
    [Theory]
    [InlineData("POSTGRES_PASSWORD")]
    [InlineData("MUSICHUB_JWT_SECRET")]
    [InlineData("MUSICHUB_PUBLIC_URL")]
    [InlineData("MUSICHUB_ACME_EMAIL")]
    [InlineData("MUSICHUB_PUBLIC_HOSTNAME")]
    public void A_missing_required_variable_is_rejected_rather_than_silently_empty(string variable)
    {
        var path = Path.Combine(FindRepoRoot(), ComposeRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(path);

        Assert.Contains($"${{{variable}:?", text, StringComparison.Ordinal);
    }

    private const string LanOverlayRelativePath = "deploy/docker/docker-compose.lan.yml";

    /// <summary>
    /// The LAN overlay exists so the dashboard can be reached by the server's LAN address without a
    /// public DNS name. It must stay a narrow, explicitly addressed publication of the API:
    ///
    /// <list type="bullet">
    /// <item>the host side of the mapping comes from <c>MUSICHUB_LAN_ADDRESS</c> /
    /// <c>MUSICHUB_LAN_PORT</c> and never from a literal, so no machine's addressing is committed and
    /// two installs can choose different ports;</item>
    /// <item>both variables are <c>:?</c>-required, so a missing value fails the deployment instead
    /// of binding the API somewhere unintended;</item>
    /// <item>only the <c>api</c> service gains a published port - never PostgreSQL, never the
    /// worker;</item>
    /// <item>the container port stays 5080, the address the API actually listens on;</item>
    /// <item>nothing in the overlay weakens TLS. Caddy keeps validating certificates.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void The_lan_overlay_publishes_only_the_api_on_an_operator_chosen_address()
    {
        var path = Path.Combine(FindRepoRoot(), LanOverlayRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{path} is missing; the LAN access path is documented but not provided.");

        var lines = File.ReadAllLines(path);
        var services = new List<string>();
        var inServices = false;
        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var indent = IndentOf(raw);
            if (indent == 0 && trimmed == "services:") { inServices = true; continue; }
            if (indent == 0 && inServices) { inServices = false; }
            if (inServices && indent == 2 && trimmed.EndsWith(':'))
            {
                services.Add(trimmed[..^1]);
            }
        }

        Assert.Equal(new[] { "api" }, services.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        var portMappings = lines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- \"", StringComparison.Ordinal) && line.Contains(":5080", StringComparison.Ordinal))
            .ToArray();
        Assert.True(portMappings.Length > 0, "The LAN overlay must publish the API's 5080 port.");

        foreach (var mapping in portMappings)
        {
            var value = mapping.TrimStart('-', ' ').Trim('"');

            // The mapping must be exactly "<address variable>:<port variable>:<container port>".
            // Splitting on ':' cannot work because the mandatory-variable form contains a colon
            // (${VAR:?message}), so the shape is matched as a whole.
            var match = System.Text.RegularExpressions.Regex.Match(
                value,
                @"^\$\{(?<address>[A-Za-z_][A-Za-z0-9_]*):(?:\?[^}]*|-[^}]*)\}:\$\{(?<port>[A-Za-z_][A-Za-z0-9_]*):(?:\?[^}]*|-[^}]*)\}:(?<target>\d+)$");

            Assert.True(match.Success, $"The LAN overlay mapping '{value}' must interpolate both the host address and the host port from variables.");

            // The host side must be interpolated from the environment
            Assert.Equal("MUSICHUB_LAN_ADDRESS", match.Groups["address"].Value);
            Assert.Contains(match.Groups["port"].Value, new[] { "MUSICHUB_LAN_PORT", "MUSICHUB_DASHBOARD_PORT" });

            // The container port is the API's real listener, not an operator choice.
            Assert.Equal("5080", match.Groups["target"].Value);
        }

        // No hard-coded RFC1918 literal may remain anywhere in the file.
        var text = File.ReadAllText(path);
        Assert.DoesNotMatch(@"\b(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)\d+\.\d+\b", text);

        Assert.DoesNotContain("skip_verify", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tls internal", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auto_https off", text, StringComparison.OrdinalIgnoreCase);

        // The database port must never be published by the overlay. Only configuration lines count:
        // the header comment deliberately tells the operator not to add it.
        var configuration = lines
            .Where(line => !line.TrimStart().StartsWith('#'))
            .ToArray();
        Assert.DoesNotContain(configuration, line => line.Contains("5432", StringComparison.Ordinal));
    }
}
