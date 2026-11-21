using MusicHub.Infrastructure.Security;
using MusicHub.Infrastructure.Telegram;
using Npgsql;
using System.Text.Json;

namespace MusicHub.Install;

/// <summary>
/// The only supported way to test a bot token from a command line.
///
/// <para>
/// Without it, the obvious thing for an operator to do is
/// <c>curl https://api.telegram.org/bot&lt;token&gt;/getMe</c>, which puts the live token into shell
/// history, the process list and terminal scrollback, and echoes it again inside the URL of any
/// failing request. This helper takes the token from the environment or stdin, never prints it, and
/// reports only the non-secret facts Telegram returns.
/// </para>
/// <para>
/// It is deliberately <b>read-only</b>: <c>getMe</c> and <c>getWebhookInfo</c> only. It never sends a
/// message, not even a test one, because a chat message is visible to end users and cannot be undone.
/// </para>
/// </summary>
public static class TelegramTokenCheck
{
    public const string TokenEnvironmentVariable = "MUSICHUB_TG_TOKEN";

    public static async Task<int> RunAsync()
    {
        var token = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token))
        {
            // Not accepted as an argument on purpose: arguments are visible to other processes and
            // are retained in the shell history file.
            if (!Console.IsInputRedirected)
            {
                Console.Error.WriteLine(
                    $"Set {TokenEnvironmentVariable} or pipe the token on stdin. It is not accepted " +
                    "as an argument because arguments are visible to other processes and to shell history.");
                return 2;
            }

            token = Console.ReadLine();
        }

        token = token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("No token was supplied.");
            return 2;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var client = new TelegramBotClient(http);
        const string ApiBase = "https://api.telegram.org";

        try
        {
            var me = await client.GetMeAsync(token, ApiBase, CancellationToken.None);
            Console.WriteLine(
                $"Authenticated. botId={me.Id} username={me.Username ?? "(none)"} firstName={me.FirstName ?? "(none)"}");
        }
        catch (Exception ex) when (ex is TelegramBotException or HttpRequestException or TaskCanceledException)
        {
            // Scrub by value, not only by pattern: the token rides inside the request URL, and a
            // transport error can echo that URL back. ScrubPatterns knows password=value shapes
            // but would leave a bare "123:ABC..." untouched.
            Console.Error.WriteLine($"Rejected: {SecretRedactor.Scrub(ex.Message, token)}");
            return 2;
        }

        try
        {
            // A webhook and long polling are mutually exclusive: with a webhook set, getUpdates
            // fails with 409, so the worker would never receive anything. Worth reporting up front.
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{ApiBase}/bot{token}/getWebhookInfo");
            using var response = await http.SendAsync(request, CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
                && root.TryGetProperty("result", out var result)
                && result.TryGetProperty("url", out var url))
            {
                var configured = url.GetString();
                Console.WriteLine(string.IsNullOrWhiteSpace(configured)
                    ? "Long polling is available: no webhook is set."
                    : "A webhook is set, so long polling will fail with 409. Remove it before starting the worker.");
            }
            else
            {
                Console.WriteLine("Could not read the webhook state; treat long polling as unverified.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.Error.WriteLine(
                $"Could not read the webhook state: {SecretRedactor.Scrub(ex.Message, token)}");
        }

        return 0;
    }

    /// <summary>
    /// The exact statement used to record the administrator.
    ///
    /// <para>
    /// Exposed so a test can assert it targets exactly one row. It must resolve the same row the
    /// API resolves - <c>TelegramEndpoints</c> reads
    /// <c>db.TelegramConfigurations.OrderBy(x => x.UpdatedAt).FirstOrDefault()</c> - because
    /// <c>TelegramConfigurations</c> has only a primary key and no singleton constraint, so nothing
    /// stops a database from containing more than one row.
    /// </para>
    /// </summary>
    public const string SetAdminSql =
        "update \"TelegramConfigurations\" " +
        "set \"AdminTelegramUserId\" = $1, " +
        "    \"AdminChatId\" = coalesce($2, \"AdminChatId\"), " +
        "    \"UpdatedAt\" = now() " +
        "where \"Id\" = (select \"Id\" from \"TelegramConfigurations\" order by \"UpdatedAt\" limit 1) " +
        "returning \"IngestionEnabled\"";

    /// <summary>
    /// Record the Telegram administrator the bot will accept audio from.
    ///
    /// <para>
    /// This is the real authorization boundary: <c>TelegramIngestionService</c> compares every
    /// sender against <c>AdminTelegramUserId</c> and rejects everyone else with a logged warning, so
    /// the value here - not anything the client claims - decides who may add music.
    /// </para>
    /// </summary>
    public static async Task<int> SetAdminAsync(string connectionString, long adminUserId, long? adminChatId)
    {
        if (adminUserId <= 0)
        {
            Console.Error.WriteLine("--admin-user-id must be a positive Telegram numeric user id.");
            return 2;
        }

        await using var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync();
        }
        catch (NpgsqlException ex)
        {
            // A failed Npgsql connection can carry the connect string, including its password.
            Console.Error.WriteLine(
                $"Could not connect to PostgreSQL: {SecretRedactor.Scrub(ex.Message, connectionString)}");
            return 2;
        }

        await using var command = connection.CreateCommand();
        // Exactly one row, and the same one the API resolves. Without the WHERE clause this
        // statement rewrites every row, so on a database holding more than one it reports success
        // while the application keeps reading a row that was never changed - the bot would then
        // reject every message with no error to explain it. Found by running the command against a
        // database that already contained a real configuration row.
        command.CommandText = SetAdminSql;
        command.Parameters.AddWithValue(adminUserId);
        command.Parameters.AddWithValue(adminChatId is null ? DBNull.Value : adminChatId.Value);

        NpgsqlDataReader reader;
        try
        {
            reader = await command.ExecuteReaderAsync();
        }
        catch (PostgresException ex)
        {
            // 42P01 = undefined_table, which is what a database that has never been migrated looks
            // like. Say so plainly instead of surfacing raw SQLSTATE noise.
            if (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                Console.Error.WriteLine(
                    "The TelegramConfigurations table does not exist. Start the API once so migrations " +
                    "run, then re-run this command.");
                return 2;
            }
            throw;
        }

        await using (reader)
        {
            if (!await reader.ReadAsync())
            {
                Console.Error.WriteLine(
                    "No Telegram configuration row exists yet. Save the configuration in the dashboard " +
                    "first so the bot token is stored, then re-run this command.");
                return 2;
            }

            Console.WriteLine(
                $"Administrator Telegram user id set to {adminUserId}" +
                (adminChatId is null ? "." : $", chat id set to {adminChatId}.") +
                $" Ingestion enabled: {reader.GetBoolean(0)}.");
        }

        return 0;
    }
}