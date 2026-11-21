using MusicHub.Domain;
using MusicHub.Infrastructure.Security;
using Npgsql;

namespace MusicHub.Install;

/// <summary>
/// Offline recovery for a lost administrator password.
///
/// <para>
/// There is deliberately no HTTP endpoint for this. The API hashes passwords with PBKDF2-SHA256, so
/// a forgotten password can only be <b>replaced</b>, never recovered - and the only credential that
/// may replace it is access to the host. A network endpoint that resets an administrator password
/// without a current session is an authentication bypass waiting for a missing rate limit, so the
/// boundary here is the shell, not the wire.
/// </para>
/// <para>
/// The new password is read from an environment variable or stdin, never from an argument: arguments
/// are visible in the process list and are retained in the shell history file. It is never printed
/// back, and the connection string is scrubbed from any driver error that could quote it.
/// </para>
/// <para>
/// Resetting also rotates <c>SecurityStamp</c> and revokes every refresh token and session for that
/// user, so a password that leaked - or an attacker holding a live token - loses access at the same
/// moment the new password takes effect.
/// </para>
/// </summary>
public static class AdminPasswordReset
{
    public const string PasswordEnvironmentVariable = "MUSICHUB_NEW_ADMIN_PASSWORD";

    /// <summary>Mirrors the API's minimum, so the CLI cannot set a password the UI would reject.</summary>
    public const int MinimumPasswordLength = 12;

    /// <summary>
    /// Update the password, rotate the security stamp and clear any lockout. Exactly one row, chosen
    /// by normalized email, because two accounts could otherwise share an address case-insensitively.
    /// </summary>
    public const string ResetPasswordSql =
        "update \"Users\" set " +
        "    \"PasswordHash\" = $2, " +
        "    \"SecurityStamp\" = $3, " +
        "    \"FailedLoginCount\" = 0, " +
        "    \"LockoutEnd\" = null, " +
        "    \"UpdatedAt\" = now() " +
        "where \"Id\" = $1 " +
        "returning \"Email\"";

    public const string RevokeRefreshTokensSql =
        "update \"RefreshTokens\" set \"RevokedAt\" = now() " +
        "where \"UserId\" = $1 and \"RevokedAt\" is null";

    public const string RevokeSessionsSql =
        "update \"UserSessions\" set \"RevokedAt\" = now() " +
        "where \"UserId\" = $1 and \"RevokedAt\" is null";

    /// <summary>
    /// How many accounts would still be able to administer the instance after this change. Used to
    /// warn when the operator is about to leave the deployment with no usable administrator, which
    /// is the same dead end this command exists to escape.
    /// </summary>
    public const string ActiveAdministratorCountSql =
        "select count(*) " +
        "from \"Users\" u " +
        "join \"UserRoles\" ur on ur.\"UserId\" = u.\"Id\" " +
        "join \"Roles\" r on r.\"Id\" = ur.\"RoleId\" " +
        "where r.\"Name\" = $1 and u.\"IsActive\" = true";

    public static async Task<int> RunAsync(string connectionString, string email, string password)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (normalizedEmail.Length == 0 || !normalizedEmail.Contains('@'))
        {
            Console.Error.WriteLine("--email must be a valid address.");
            return 2;
        }

        if (password.Length < MinimumPasswordLength)
        {
            Console.Error.WriteLine(
                $"The new password must be at least {MinimumPasswordLength} characters. " +
                "The API enforces the same minimum, so a shorter value would be rejected at login setup.");
            return 2;
        }

        await using var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync();
        }
        catch (NpgsqlException ex)
        {
            Console.Error.WriteLine(
                $"Could not connect to PostgreSQL: {SecretRedactor.Scrub(ex.Message, connectionString, password)}");
            return 2;
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync();

            var userId = await FindUserIdAsync(connection, transaction, normalizedEmail);
            if (userId is null)
            {
                await transaction.RollbackAsync();
                Console.Error.WriteLine(
                    $"No account has the address '{normalizedEmail}'. " +
                    "Pass --email with the exact address of an existing administrator.");
                return 2;
            }

            var securityStamp = Guid.NewGuid().ToString("N");
            var passwordHash = PasswordHasher.Hash(password);

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = ResetPasswordSql;
                update.Parameters.AddWithValue(userId.Value);
                update.Parameters.AddWithValue(passwordHash);
                update.Parameters.AddWithValue(securityStamp);
                await update.ExecuteNonQueryAsync();
            }

            int revokedTokens;
            await using (var revoke = connection.CreateCommand())
            {
                revoke.Transaction = transaction;
                revoke.CommandText = RevokeRefreshTokensSql;
                revoke.Parameters.AddWithValue(userId.Value);
                revokedTokens = await revoke.ExecuteNonQueryAsync();
            }

            int revokedSessions;
            await using (var revoke = connection.CreateCommand())
            {
                revoke.Transaction = transaction;
                revoke.CommandText = RevokeSessionsSql;
                revoke.Parameters.AddWithValue(userId.Value);
                revokedSessions = await revoke.ExecuteNonQueryAsync();
            }

            await WriteAuditAsync(connection, transaction, userId.Value, normalizedEmail);

            var remainingAdmins = await CountActiveAdministratorsAsync(connection, transaction);

            await transaction.CommitAsync();

            Console.WriteLine($"Password replaced for '{normalizedEmail}'.");
            Console.WriteLine(
                $"Sessions invalidated: {revokedTokens} refresh token(s), {revokedSessions} device session(s).");
            Console.WriteLine("The new password was not printed. Sign in with it and store it in a password manager.");

            if (remainingAdmins == 0)
            {
                Console.Error.WriteLine(
                    "WARNING: no active administrator remains. Create or reactivate one before relying on " +
                    "this instance, or run this command again for another account.");
            }

            return 0;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            Console.Error.WriteLine(
                "The Users table does not exist. Start the API once so migrations run, then re-run this command.");
            return 2;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            // A driver error can quote the statement parameters, so scrub by value as well as shape.
            Console.Error.WriteLine($"Reset failed: {SecretRedactor.Scrub(ex.Message, password)}");
            return 2;
        }
        finally
        {
            password = string.Empty;
        }
    }

    private static async Task<Guid?> FindUserIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string normalizedEmail)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The API stores NormalizedEmail as the lower-cased address and looks users up by it, so
        // matching on the same column keeps the CLI and the login path pointing at one row.
        command.CommandText = "select \"Id\" from \"Users\" where \"NormalizedEmail\" = $1 limit 1";
        command.Parameters.AddWithValue(normalizedEmail);
        var result = await command.ExecuteScalarAsync();
        return result is Guid id ? id : null;
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        string normalizedEmail)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // ActorUserId stays null: no authenticated user performed this, an operator with host access
        // did. Recording a fake actor would misrepresent the audit trail.
        command.CommandText =
            "insert into \"AuditLogs\" " +
            "(\"ActorUserId\", \"Action\", \"EntityType\", \"EntityId\", \"DetailsJson\", \"IpAddress\", \"CreatedAt\") " +
            "values (null, $1, $2, $3, $4, null, now())";
        command.Parameters.AddWithValue("admin.password.reset");
        command.Parameters.AddWithValue("User");
        command.Parameters.AddWithValue(userId.ToString());
        command.Parameters.AddWithValue(
            System.Text.Json.JsonSerializer.Serialize(new { via = "install-cli", email = normalizedEmail }));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountActiveAdministratorsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ActiveAdministratorCountSql;
        command.Parameters.AddWithValue(RoleNames.Administrator);
        var result = await command.ExecuteScalarAsync();
        return result is long count ? count : Convert.ToInt64(result ?? 0L);
    }
}
