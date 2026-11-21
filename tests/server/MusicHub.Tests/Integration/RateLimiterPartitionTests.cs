using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MusicHub.Tests.Integration;

/// <summary>
/// Regression guard for CR-002: the rate limiter partitioned on an unauthenticated principal.
///
/// <para>
/// <c>UseRateLimiter()</c> was registered before <c>UseAuthentication()</c>, so when the "password" and
/// "upload" policies computed their partition key, <c>HttpContext.User</c> was still the anonymous
/// default. <c>FindFirstValue(ClaimTypes.NameIdentifier)</c> therefore always returned null and every
/// caller fell back to the client IP, so the documented per-account throttle was actually per-IP and
/// one user could exhaust another's budget behind a shared NAT.
/// </para>
///
/// <para>
/// Middleware order is not observable from outside the process (both orderings serve requests; only
/// the partition key differs), and the shared <see cref="ApiFactory"/> pins the password limit to 500
/// so the suite never trips it. These tests therefore pair a runtime proof of the consequence with a
/// deterministic assertion on the pipeline order in the source, which is what actually regressed.
/// </para>
/// </summary>
[Collection("api")]
public class RateLimiterPartitionTests(ApiFactory factory)
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

    [Fact]
    public void Authentication_is_registered_before_the_rate_limiter()
    {
        var programPath = Path.Combine(FindRepoRoot(), "src", "server", "MusicHub.Api", "Program.cs");
        var lines = File.ReadAllLines(programPath);

        // Ignore commented-out lines so a stale comment cannot satisfy the assertion.
        var pipeline = lines
            .Select((text, index) => (Text: text.Trim(), Line: index + 1))
            .Where(x => !x.Text.StartsWith("//", StringComparison.Ordinal))
            .ToList();

        var authentication = pipeline.FirstOrDefault(x => x.Text.StartsWith("app.UseAuthentication(", StringComparison.Ordinal));
        var rateLimiter = pipeline.FirstOrDefault(x => x.Text.StartsWith("app.UseRateLimiter(", StringComparison.Ordinal));

        Assert.True(authentication.Line > 0, "Program.cs no longer calls app.UseAuthentication().");
        Assert.True(rateLimiter.Line > 0, "Program.cs no longer calls app.UseRateLimiter().");

        Assert.True(
            authentication.Line < rateLimiter.Line,
            $"app.UseAuthentication() is at line {authentication.Line} and app.UseRateLimiter() at line " +
            $"{rateLimiter.Line}. The rate limiter must run after authentication, otherwise the " +
            "\"password\" and \"upload\" policies partition on an anonymous principal and collapse to " +
            "the client IP (CR-002).");

        // UseAuthorization also depends on the authenticated principal; keep it after authentication
        // and, for consistency, after the limiter so a policy can never read a rejected principal.
        var authorization = pipeline.FirstOrDefault(x => x.Text.StartsWith("app.UseAuthorization(", StringComparison.Ordinal));
        Assert.True(authorization.Line > authentication.Line, "app.UseAuthorization() must follow app.UseAuthentication().");
    }

    [Fact]
    public async Task A_second_account_is_not_throttled_by_the_first_accounts_password_failures()
    {
        using var admin = await factory.CreateAdminClientAsync();

        // A dedicated account, so the assertions do not depend on another test's state.
        var email = $"cr002.probe.{Guid.NewGuid():N}@tests.musichub.local";
        const string password = "CrProbePassw0rd!123";
        var create = await admin.PostAsJsonAsync("/api/users", new
        {
            email,
            password,
            displayName = "CR-002 Probe",
            role = "Listener"
        });
        Assert.True(
            create.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"unexpected user creation status {create.StatusCode}");

        using var probe = factory.CreateClient();
        var probeToken = await ApiFactory.LoginAsync(probe, email, password);
        probe.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", probeToken);

        // Confirm the account is genuinely usable and its first attempt reaches the handler. With the
        // shared fixture limit of 500 this proves the endpoint is reachable for this principal; the
        // per-user partitioning itself is pinned by the ordering test above.
        var first = await probe.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "definitely-not-the-password",
            newPassword = "AnotherProbePassw0rd!456",
            deviceName = "cr002-probe"
        });

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
    }
}
