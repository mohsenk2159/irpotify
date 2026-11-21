using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MusicHub.Tests.Integration;

[Collection("api")]
public class AuthorizationAndTelegramTests(ApiFactory factory)
{
    private const string FakeBotToken = "123456789:AAFakeTokenValueForTestingOnly_1234567890";

    [Fact]
    public async Task Listener_role_is_denied_admin_operations_and_tokens_stay_masked()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var create = await admin.PostAsJsonAsync("/api/users", new
        {
            email = ApiFactory.ListenerEmail,
            password = ApiFactory.ListenerPassword,
            displayName = "Integration Listener",
            role = "Listener"
        });
        Assert.True(create.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);

        using var listener = factory.CreateClient();
        var listenerToken = await ApiFactory.LoginAsync(listener, ApiFactory.ListenerEmail, ApiFactory.ListenerPassword);
        listener.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", listenerToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/api/admin/telegram")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/api/admin/metrics")).StatusCode);

        var config = await admin.GetFromJsonAsync<JsonElement>("/api/admin/telegram");
        Assert.False(config.GetProperty("configured").GetBoolean());

        var invalidFormat = await admin.PutAsJsonAsync("/api/admin/telegram", new { botToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidFormat.StatusCode);

        var saved = await admin.PutAsJsonAsync("/api/admin/telegram", new
        {
            botToken = FakeBotToken,
            adminTelegramUserId = 1000001,
            adminChatId = 1000001,
            ingestionEnabled = false,
            destinationGroupId = -1001234567890,
            autoPublishEnabled = false
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var savedRaw = await saved.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeBotToken, savedRaw);
        using var savedJson = JsonDocument.Parse(savedRaw);
        Assert.True(savedJson.RootElement.GetProperty("tokenConfigured").GetBoolean());

        var readBack = await admin.GetAsync("/api/admin/telegram");
        Assert.Equal(HttpStatusCode.OK, readBack.StatusCode);
        Assert.DoesNotContain(FakeBotToken, await readBack.Content.ReadAsStringAsync());
    }
}
