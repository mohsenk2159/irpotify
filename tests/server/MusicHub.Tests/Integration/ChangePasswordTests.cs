using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace MusicHub.Tests.Integration;

[Collection("api")]
public class ChangePasswordTests(ApiFactory factory)
{
    private const string Password = "ChangePassw0rd!123";
    private const string NewPassword = "NewChangePassw0rd!456";

    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}@changepw.tests.musichub.local";

    private async Task<(HttpClient Client, string AccessToken, string RefreshToken)> LoginAsync(string email, string password, string deviceName = "change-password-tests")
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password, deviceName, clientType = "player" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = json.RootElement.GetProperty("accessToken").GetString()!;
        var refreshToken = json.RootElement.GetProperty("refreshToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (client, accessToken, refreshToken);
    }

    private async Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string currentPassword, string newPassword)
    {
        return await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword,
            newPassword,
            deviceName = "change-password-tests"
        });
    }

    private async Task<Guid> CreateUserAsync(string email, string password)
    {
        using var admin = await factory.CreateAdminClientAsync();
        var create = await admin.PostAsJsonAsync("/api/users", new
        {
            email,
            password,
            displayName = email.Split('@')[0],
            role = "Listener",
            canUsePlayer = true,
            quotaBytes = 0
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var json = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Change_password_rotates_tokens_and_honors_new_credentials()
    {
        var email = Unique("rotate");
        await CreateUserAsync(email, Password);
        var (user, oldAccessToken, oldRefreshToken) = await LoginAsync(email, Password);

        var change = await ChangePasswordAsync(user, Password, NewPassword);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        string? newAccessToken;
        string? newRefreshToken;
        using (var changeJson = JsonDocument.Parse(await change.Content.ReadAsStringAsync()))
        {
            newAccessToken = changeJson.RootElement.GetProperty("accessToken").GetString();
            newRefreshToken = changeJson.RootElement.GetProperty("refreshToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(newAccessToken));
            Assert.False(string.IsNullOrWhiteSpace(newRefreshToken));
        }

        // The pre-change refresh token must now be rejected.
        var oldRefreshCall = await user.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldRefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefreshCall.StatusCode);

        // The newly issued refresh token must work.
        var rotated = await user.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = newRefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        using (var rotatedJson = JsonDocument.Parse(await rotated.Content.ReadAsStringAsync()))
        {
            var secondRefresh = rotatedJson.RootElement.GetProperty("refreshToken").GetString();
            Assert.NotEqual(newRefreshToken, secondRefresh);
        }

        // The pre-change access token must be rejected because the security stamp rotated.
        var preChangeCall = await user.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, preChangeCall.StatusCode);

        // The new credentials must work for a fresh login.
        var relogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = NewPassword, deviceName = "change-password-tests", clientType = "player" });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task Change_password_with_wrong_current_password_fails()
    {
        var email = Unique("wrong");
        await CreateUserAsync(email, Password);
        var (user, _, _) = await LoginAsync(email, Password);

        var change = await ChangePasswordAsync(user, "DefinitelyWrongCurrentPassword123", "NewChangePassw0rd!456");
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
    }

    [Fact]
    public async Task Change_password_with_short_new_password_fails()
    {
        var email = Unique("short");
        await CreateUserAsync(email, Password);
        var (user, _, _) = await LoginAsync(email, Password);

        var change = await ChangePasswordAsync(user, Password, "short");
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
    }
}
