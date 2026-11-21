using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MusicHub.Tests.Integration;

[Collection("api")]
public class AuthFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Bootstrap_login_me_and_refresh_rotation_are_enforced()
    {
        using var client = factory.CreateClient();
        await ApiFactory.EnsureInitializedAsync(client);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/setup/status");
        Assert.True(status.GetProperty("initialized").GetBoolean());

        var secondBootstrap = await client.PostAsJsonAsync("/api/setup/initialize", new
        {
            bootstrapToken = ApiFactory.BootstrapToken,
            email = "second@tests.musichub.local",
            password = "SecondAdminPassw0rd!123",
            displayName = "Second"
        });
        Assert.Equal(HttpStatusCode.Conflict, secondBootstrap.StatusCode);

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.AdminEmail,
            password = "definitely-wrong",
            deviceName = "integration-tests"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.AdminEmail,
            password = ApiFactory.AdminPassword,
            deviceName = "integration-tests"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = loginJson.RootElement.GetProperty("accessToken").GetString();
        var refreshToken = loginJson.RootElement.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        var anonymousMe = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousMe.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(ApiFactory.AdminEmail, me.GetProperty("email").GetString());

        client.DefaultRequestHeaders.Authorization = null;
        var rotated = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        using var rotatedJson = JsonDocument.Parse(await rotated.Content.ReadAsStringAsync());
        var newRefresh = rotatedJson.RootElement.GetProperty("refreshToken").GetString();
        Assert.NotEqual(refreshToken, newRefresh);

        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var afterRevocation = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = newRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevocation.StatusCode);
    }
}
