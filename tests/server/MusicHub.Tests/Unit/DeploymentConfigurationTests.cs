using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using MusicHub.Api;

namespace MusicHub.Tests.Unit;

/// <summary>
/// Unit tests for the two deployment defects closed in W-019.
///
/// <para>
/// B-020: <c>MUSICHUB_PUBLIC_URL</c> was read with <c>??</c>, so an empty string counted as
/// "configured" and the pairing payload became <c>musichub://pair?server=&amp;code=...</c>, which no
/// client can parse. A blank value must mean "unset", and a value that is present but not an
/// absolute http(s) URL must fail loudly rather than emit a silently wrong QR code.
/// </para>
///
/// <para>
/// B-021: no forwarded-header middleware existed, so behind a reverse proxy every request appeared
/// to come from the proxy and the HTTPS scheme was lost. The replacement must not repeat the
/// framework's blanket "trust every peer" behaviour, because that lets a direct caller forge
/// <c>X-Forwarded-For</c> and escape the per-IP rate limit.
/// </para>
/// </summary>
public sealed class DeploymentConfigurationTests : IDisposable
{
    private readonly string? _originalPublicUrl;

    public DeploymentConfigurationTests()
    {
        // The tests own this variable, so capture and restore whatever the developer machine
        // happens to have set rather than assuming a clean environment.
        _originalPublicUrl = Environment.GetEnvironmentVariable(PublicUrl.EnvironmentVariable);
        Environment.SetEnvironmentVariable(PublicUrl.EnvironmentVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PublicUrl.EnvironmentVariable, _originalPublicUrl);
    }

    private static HttpContext Request(string url = "http://127.0.0.1:5080/api/pairing/create")
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = new Uri(url).Scheme;
        context.Request.Host = HostString.FromUriComponent(new Uri(url));
        return context;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    // ---------------------------------------------------------------- B-020

    [Fact]
    public void PublicUrl_Unset_fallsBackToTheRequestOrigin()
    {
        var resolved = PublicUrl.Resolve(Request(), Config());

        Assert.Equal("http://127.0.0.1:5080", resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void PublicUrl_BlankEnvironmentVariable_countsAsUnset(string blank)
    {
        // This is the regression: an empty string used to win and produce "server=".
        Environment.SetEnvironmentVariable(PublicUrl.EnvironmentVariable, blank);

        var resolved = PublicUrl.Resolve(Request(), Config());

        Assert.Equal("http://127.0.0.1:5080", resolved);
        Assert.DoesNotContain("server=&", $"musichub://pair?server={Uri.EscapeDataString(resolved)}&code=x");
    }

    [Fact]
    public void PublicUrl_BlankConfigurationValue_countsAsUnset()
    {
        var resolved = PublicUrl.Resolve(Request(), Config((PublicUrl.ConfigurationKey, "  ")));

        Assert.Equal("http://127.0.0.1:5080", resolved);
    }

    [Fact]
    public void PublicUrl_EnvironmentVariableWinsOverConfiguration()
    {
        Environment.SetEnvironmentVariable(PublicUrl.EnvironmentVariable, "https://env.example.com");

        var resolved = PublicUrl.Resolve(Request(), Config((PublicUrl.ConfigurationKey, "https://config.example.com")));

        Assert.Equal("https://env.example.com", resolved);
    }

    [Fact]
    public void PublicUrl_ConfigurationIsUsedWhenTheEnvironmentVariableIsUnset()
    {
        var resolved = PublicUrl.Resolve(Request(), Config((PublicUrl.ConfigurationKey, "https://config.example.com")));

        Assert.Equal("https://config.example.com", resolved);
    }

    [Fact]
    public void PublicUrl_TrailingSlashIsRemoved()
    {
        Environment.SetEnvironmentVariable(PublicUrl.EnvironmentVariable, "https://music.example.com/");

        // A trailing slash would otherwise produce "//api/..." in derived links and a double
        // slash inside the pairing payload.
        Assert.Equal("https://music.example.com", PublicUrl.Resolve(Request(), Config()));
    }

    [Theory]
    [InlineData("music.example.com")]        // no scheme: not absolute
    [InlineData("/relative/path")]           // relative
    [InlineData("ftp://music.example.com")]  // wrong scheme
    [InlineData("https://")]                 // no host
    [InlineData("http:// bad host")]         // invalid host
    public void PublicUrl_MalformedValue_failsLoudlyInsteadOfEmittingAWrongQrCode(string value)
    {
        Environment.SetEnvironmentVariable(PublicUrl.EnvironmentVariable, value);

        var error = Assert.Throws<ApiException>(() => PublicUrl.Resolve(Request(), Config()));

        Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
        // The message must name the setting so the operator can find it, without echoing the
        // request or any secret material.
        Assert.Contains(PublicUrl.EnvironmentVariable, error.Message);
    }

    [Fact]
    public void PublicUrl_HttpIsAcceptedForLoopbackDevelopment()
    {
        Environment.SetEnvironmentVariable(PublicUrl.EnvironmentVariable, "http://192.168.10.20:5080");

        Assert.Equal("http://192.168.10.20:5080", PublicUrl.Resolve(Request(), Config()));
    }

    [Fact]
    public void PublicUrl_ResolvedValueProducesAParseablePairingPayload()
    {
        // The end-to-end assertion for B-020: whatever Resolve returns must survive the payload
        // construction with a non-empty server component.
        Environment.SetEnvironmentVariable(PublicUrl.EnvironmentVariable, "");
        var serverUrl = PublicUrl.Resolve(Request(), Config());

        var payload = $"musichub://pair?server={Uri.EscapeDataString(serverUrl)}&code=abc&expires=1";
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(payload).Query);

        Assert.False(string.IsNullOrWhiteSpace(query["server"]));
        Assert.Equal(serverUrl, query["server"]);
    }

    // ---------------------------------------------------------------- B-021

    [Fact]
    public void ForwardedHeaders_NotConfigured_staysDisabled()
    {
        var options = ForwardedHeadersSetup.Configure(Config(), out var configured);

        Assert.False(configured);
        // Even when the caller ignores the flag, the options must not trust the world.
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void ForwardedHeaders_ClearsTheFrameworkLoopbackDefaults()
    {
        // ForwardedHeadersOptions ships with loopback in KnownProxies/KnownNetworks. Keeping them
        // would silently trust 127.0.0.1 even when the operator configured nothing else.
        var options = ForwardedHeadersSetup.Configure(
            Config((ForwardedHeadersSetup.KnownProxiesKey, "10.1.2.3")),
            out var configured);

        Assert.True(configured);
        Assert.Equal([System.Net.IPAddress.Parse("10.1.2.3")], options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void ForwardedHeaders_KnownNetworksAcceptsASingleCidr()
    {
        var options = ForwardedHeadersSetup.Configure(
            Config((ForwardedHeadersSetup.KnownNetworksKey, "172.28.0.0/24")),
            out var configured);

        Assert.True(configured);
        var network = Assert.Single(options.KnownIPNetworks);
        Assert.True(network.Contains(System.Net.IPAddress.Parse("172.28.0.5")));
        Assert.False(network.Contains(System.Net.IPAddress.Parse("172.29.0.5")));
    }

    [Fact]
    public void ForwardedHeaders_KnownNetworksAcceptsACommaSeparatedList()
    {
        // The compose file and .env pass a comma-separated string, not a JSON array. Binding only
        // the array form would silently disable the middleware.
        var options = ForwardedHeadersSetup.Configure(
            Config((ForwardedHeadersSetup.KnownNetworksKey, "172.28.0.0/24, 10.0.0.0/8")),
            out var configured);

        Assert.True(configured);
        Assert.Equal(2, options.KnownIPNetworks.Count);
    }

    [Fact]
    public void ForwardedHeaders_KnownNetworksAcceptsAnArray()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownNetworks:0"] = "172.28.0.0/24",
                ["ForwardedHeaders:KnownNetworks:1"] = "::1/128",
            })
            .Build();

        var options = ForwardedHeadersSetup.Configure(configuration, out var configured);

        Assert.True(configured);
        Assert.Equal(2, options.KnownIPNetworks.Count);
    }

    [Fact]
    public void ForwardedHeaders_BareAddressIsTreatedAsAHostRoute()
    {
        var options = ForwardedHeadersSetup.Configure(
            Config((ForwardedHeadersSetup.KnownNetworksKey, "10.9.8.7")),
            out var configured);

        Assert.True(configured);
        var network = Assert.Single(options.KnownIPNetworks);
        Assert.True(network.Contains(System.Net.IPAddress.Parse("10.9.8.7")));
        Assert.False(network.Contains(System.Net.IPAddress.Parse("10.9.8.8")));
    }

    [Theory]
    [InlineData("proxy.internal")]      // hostname: DNS would decide who may assert the client IP
    [InlineData("172.28.0.0/33")]       // prefix out of range
    [InlineData("172.28.0.0/-1")]       // negative prefix
    [InlineData("172.28.0.0/abc")]      // non-numeric prefix
    public void ForwardedHeaders_InvalidNetwork_isRejectedAtStartup(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ForwardedHeadersSetup.Configure(Config((ForwardedHeadersSetup.KnownNetworksKey, value)), out _));
    }

    [Fact]
    public void ForwardedHeaders_HostnameProxy_isRejected()
    {
        // Resolving a hostname here would let DNS decide the trust boundary.
        Assert.Throws<InvalidOperationException>(() =>
            ForwardedHeadersSetup.Configure(Config((ForwardedHeadersSetup.KnownProxiesKey, "caddy")), out _));
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    [InlineData("99", 10)]
    public void ForwardedHeaders_ForwardLimitIsClamped(string configured, int expected)
    {
        var options = ForwardedHeadersSetup.Configure(
            Config(
                (ForwardedHeadersSetup.KnownProxiesKey, "10.1.2.3"),
                (ForwardedHeadersSetup.ForwardLimitKey, configured)),
            out _);

        Assert.Equal(expected, options.ForwardLimit);
    }

    [Fact]
    public void ForwardedHeaders_ProcessesForAndProtoOnly()
    {
        var options = ForwardedHeadersSetup.Configure(
            Config((ForwardedHeadersSetup.KnownProxiesKey, "10.1.2.3")),
            out _);

        // X-Forwarded-Host is deliberately not processed: the operator sets MUSICHUB_PUBLIC_URL
        // explicitly, and honouring a forwarded host would let a header choose the pairing origin.
        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
    }

    [Fact]
    public void ForwardedHeaders_WhitespaceEntriesAreIgnored()
    {
        var options = ForwardedHeadersSetup.Configure(
            Config((ForwardedHeadersSetup.KnownNetworksKey, "  ,  ,")),
            out var configured);

        Assert.False(configured);
        Assert.Empty(options.KnownIPNetworks);
    }
}
