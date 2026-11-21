using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MusicHub.Api;

/// <summary>
/// Configures <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c> handling for deployments behind a
/// reverse proxy (B-021).
/// </summary>
/// <remarks>
/// <para>
/// The framework switch <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> trusts <em>every</em> peer, so a
/// direct request to the API could forge <c>X-Forwarded-For</c> to escape the per-IP rate limit or
/// poison the IP recorded in audit and session rows. Instead, trust is limited to the proxies named
/// in <c>ForwardedHeaders:KnownProxies</c> or <c>ForwardedHeaders:KnownNetworks</c>.
/// </para>
/// <para>
/// Nothing is enabled unless one of those keys is configured, so the default (no proxy, or the
/// container topology that never publishes the API port) is unchanged and cannot be influenced by
/// a header at all.
/// </para>
/// </remarks>
public static class ForwardedHeadersSetup
{
    public const string KnownProxiesKey = "ForwardedHeaders:KnownProxies";
    public const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";
    public const string ForwardLimitKey = "ForwardedHeaders:ForwardLimit";

    /// <summary>
    /// Build the options. The out parameter reports whether any proxy or network was configured;
    /// when it is false the caller must not install the middleware.
    /// </summary>
    public static ForwardedHeadersOptions Configure(IConfiguration configuration, out bool configured)
    {
        var proxies = ReadList(configuration, KnownProxiesKey);
        var networks = ReadList(configuration, KnownNetworksKey);

        // Only IPv4/IPv6 literals are accepted. Resolving a hostname here would let DNS decide who
        // may assert the client identity.
        var parsedProxies = proxies
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Select(value => IPAddress.TryParse(value, out var address)
                ? address
                : throw new InvalidOperationException(
                    $"{KnownProxiesKey} entry '{value}' is not an IP address. List proxies by address, not by hostname."))
            .ToArray();

        var parsedNetworks = networks
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Select(ParseNetwork)
            .ToArray();

        configured = parsedProxies.Length > 0 || parsedNetworks.Length > 0;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // Default is 1; a single proxy hop is enough and capping it stops a client from
            // walking the chain backwards.
            ForwardLimit = Math.Clamp(configuration.GetValue(ForwardLimitKey, 1), 1, 10),
            RequireHeaderSymmetry = false
        };

        // The defaults already contain the loopback addresses. Clearing them makes the trusted set
        // exactly what the operator configured, which is the point of the change.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in parsedProxies) options.KnownProxies.Add(proxy);
        foreach (var network in parsedNetworks) options.KnownIPNetworks.Add(network);

        return options;
    }

    private static string[] ReadList(IConfiguration configuration, string key)
    {
        // Accept both shapes the operator can realistically write:
        //   ForwardedHeaders__KnownNetworks=172.28.0.0/24,10.0.0.0/8     (environment variable)
        //   "ForwardedHeaders": { "KnownNetworks": [ "172.28.0.0/24" ] }  (appsettings / JSON)
        // Bind(string[]) only understands the array form, and a single-string value would silently
        // produce an empty list, which would disable the middleware the operator asked for.
        var section = configuration.GetSection(key);
        var array = section.Get<string[]>();
        if (array is { Length: > 0 }) return array;

        var raw = section.Value;
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return raw
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static System.Net.IPNetwork ParseNetwork(string value)
    {
        var parts = value.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var address))
        {
            throw new InvalidOperationException(
                $"{KnownNetworksKey} entry '{value}' does not start with an IP address (use CIDR, for example 10.0.0.0/8).");
        }

        if (parts.Length == 1)
        {
            var full = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
            return new System.Net.IPNetwork(address, full);
        }

        if (!int.TryParse(parts[1], out var prefix))
        {
            throw new InvalidOperationException($"{KnownNetworksKey} entry '{value}' has a non-numeric prefix length.");
        }

        var max = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        if (prefix < 0 || prefix > max)
        {
            throw new InvalidOperationException($"{KnownNetworksKey} entry '{value}' has a prefix length outside 0-{max}.");
        }

        return new System.Net.IPNetwork(address, prefix);
    }
}
