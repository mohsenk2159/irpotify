using MusicHub.Domain;
using MusicHub.Install;
using MusicHub.Infrastructure.Security;

namespace MusicHub.Tests.Unit;

/// <summary>
/// The offline administrator recovery path.
///
/// <para>
/// A forgotten administrator password has no online recovery: the API stores a PBKDF2 hash, and
/// every route that could replace it requires a session the operator no longer has. Before
/// <c>reset-admin-password</c> existed the only way back in was hand-editing the database, which is
/// neither documented nor auditable. These tests pin the properties that make the command a safe
/// substitute for that: it revokes what the old password could reach, and it never accepts or
/// prints the new password.
/// </para>
/// </summary>
public sealed class AdminPasswordResetTests
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

    private static string ReadSource(params string[] relativePath) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(relativePath).ToArray()));

    [Fact]
    public void The_reset_statement_updates_one_row_and_clears_the_lockout()
    {
        var sql = AdminPasswordReset.ResetPasswordSql;

        // Targeted by primary key: without the WHERE clause this would rewrite every account.
        Assert.Contains("where \"Id\" = $1", sql, StringComparison.Ordinal);

        // A lockout survives a password change otherwise, so the operator would still be refused.
        Assert.Contains("\"LockoutEnd\" = null", sql, StringComparison.Ordinal);
        Assert.Contains("\"FailedLoginCount\" = 0", sql, StringComparison.Ordinal);

        // The stamp must rotate: live access tokens carry it, and they belong to whoever had the
        // old password.
        Assert.Contains("\"SecurityStamp\" = $3", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reset_revokes_refresh_tokens_and_device_sessions()
    {
        // Revoking only the password leaves an attacker holding a valid refresh token signed in.
        Assert.Contains("update \"RefreshTokens\"", AdminPasswordReset.RevokeRefreshTokensSql, StringComparison.Ordinal);
        Assert.Contains("\"RevokedAt\" = now()", AdminPasswordReset.RevokeRefreshTokensSql, StringComparison.Ordinal);
        Assert.Contains("where \"UserId\" = $1", AdminPasswordReset.RevokeRefreshTokensSql, StringComparison.Ordinal);

        Assert.Contains("update \"UserSessions\"", AdminPasswordReset.RevokeSessionsSql, StringComparison.Ordinal);
        Assert.Contains("where \"UserId\" = $1", AdminPasswordReset.RevokeSessionsSql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_active_administrator_warning_counts_only_active_administrators()
    {
        var sql = AdminPasswordReset.ActiveAdministratorCountSql;

        Assert.Contains("u.\"IsActive\" = true", sql, StringComparison.Ordinal);

        // The role is bound as a parameter, never inlined: the count must not depend on a string
        // literal, and parameter binding is what keeps this statement free of injection.
        Assert.Contains("r.\"Name\" = $1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(RoleNames.Administrator, sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cli_refuses_a_password_argument_and_never_prints_one()
    {
        var program = ReadSource("src", "server", "MusicHub.Install", "Program.cs");

        // The password must arrive through the environment or stdin. An argument is visible in the
        // process list and retained in shell history.
        Assert.Contains(AdminPasswordReset.PasswordEnvironmentVariable, program, StringComparison.Ordinal);
        Assert.Contains("Passwords are not accepted as arguments", program, StringComparison.Ordinal);

        // Nothing may echo the value back: not the confirmation, not an error path.
        Assert.DoesNotContain("Console.WriteLine(password", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.WriteLine($\"{password", program, StringComparison.Ordinal);

        // The verb is reachable, otherwise the recovery path does not exist.
        Assert.Contains("reset-admin-password", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reset_writes_an_audit_row_with_no_fabricated_actor()
    {
        var source = ReadSource("src", "server", "MusicHub.Install", "AdminPasswordReset.cs");

        Assert.Contains("admin.password.reset", source, StringComparison.Ordinal);
        // An operator at a shell performed this, not an authenticated user. Recording a user id
        // here would make the audit trail claim something that did not happen.
        Assert.Contains("values (null, $1, $2, $3, $4, null, now())", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_minimum_length_matches_the_api()
    {
        // A CLI that accepted 8 characters would set a password the setup endpoint rejects, leaving
        // an account whose password cannot be reproduced through any documented flow.
        Assert.Equal(12, AdminPasswordReset.MinimumPasswordLength);
    }

    [Fact]
    public void A_hashed_password_can_be_verified_and_is_not_the_plaintext()
    {
        // Guards the assumption behind "recover" being impossible: the stored value is a hash, so
        // the command replaces rather than reveals.
        var hash = PasswordHasher.Hash("Recovery!Passw0rd");
        Assert.StartsWith("pbkdf2-sha256$", hash, StringComparison.Ordinal);
        Assert.DoesNotContain("Recovery!Passw0rd", hash, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify("Recovery!Passw0rd", hash));
        Assert.False(PasswordHasher.Verify("Recovery!Passw0rd2", hash));
    }

    /// <summary>
    /// The published Docker recipe must obtain the connection string from somewhere that exists.
    ///
    /// <para>
    /// The first version of the docs told operators to forward <c>MUSICHUB_CONNECTION_STRING</c> from
    /// their shell with <c>-e MUSICHUB_CONNECTION_STRING</c>. That variable is never set on the host:
    /// compose synthesises it from <c>POSTGRES_DB</c>/<c>POSTGRES_USER</c>/<c>POSTGRES_PASSWORD</c> and
    /// injects it into the containers only. Following the instructions verbatim therefore failed with
    /// "Format of the initialization string does not conform to specification starting at index 0" -
    /// a broken recovery path in exactly the situation it exists for. This test pins the fix.
    /// </para>
    /// </summary>
    [Fact]
    public void The_documented_docker_recovery_reads_the_connection_string_from_the_container()
    {
        var docs = ReadSource("docs", "INSTALL_AND_RUN.md");

        var dockerSection = docs.IndexOf("### Docker install", StringComparison.Ordinal);
        Assert.True(dockerSection > 0, "docs/INSTALL_AND_RUN.md no longer has a Docker recovery section.");
        var end = docs.IndexOf("### What this does not fix", dockerSection, StringComparison.Ordinal);
        Assert.True(end > dockerSection, "The Docker section is not delimited by the closing heading.");
        var section = docs[dockerSection..end];

        // The string must be sourced from the running container, which is the only place it exists.
        Assert.Contains("printenv MUSICHUB_CONNECTION_STRING", section, StringComparison.Ordinal);
        Assert.Contains("musichub-api-1", section, StringComparison.Ordinal);

        // It must be forwarded by name only, so the value never lands on a command line.
        Assert.Contains("-e MUSICHUB_CONNECTION_STRING", section, StringComparison.Ordinal);
        Assert.DoesNotContain("MUSICHUB_CONNECTION_STRING=", section.Replace("$env:MUSICHUB_CONNECTION_STRING =", ""),
            StringComparison.Ordinal);

        // The database is deliberately unpublished, so a host-side dotnet run cannot be the recipe.
        Assert.Contains("not", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("published to the host", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_documented_docker_recovery_clears_the_secrets_from_the_shell()
    {
        var docs = ReadSource("docs", "INSTALL_AND_RUN.md");

        // Leaving MUSICHUB_NEW_ADMIN_PASSWORD in the environment keeps the new password readable by
        // every later child process in that session, which defeats the point of not using argv.
        Assert.Contains("Remove-Item Env:MUSICHUB_CONNECTION_STRING", docs, StringComparison.Ordinal);
        Assert.Contains("Env:MUSICHUB_NEW_ADMIN_PASSWORD", docs, StringComparison.Ordinal);
    }
}
