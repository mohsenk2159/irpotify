using MusicHub.Infrastructure.Security;
using MusicHub.Install;

namespace MusicHub.Tests.Unit;

/// <summary>
/// Regression tests for the installer and host-startup security findings closed in W-032.
///
/// <para>
/// All four shared a failure mode: a check that could not distinguish "nothing is installed" from
/// "installed but unreadable", or a statement that changed one property while leaving a dangerous
/// one alone. The tests below assert the discriminating behaviour, not the absence of an exception.
/// </para>
/// </summary>
public sealed class InstallerSecurityTests : IDisposable
{
    private readonly string _root;

    public InstallerSecurityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "musichub-installer-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

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

    /// <summary>Lines of a source file with comment-only lines removed.</summary>
    private static List<string> ExecutableLines(params string[] relativePath)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(relativePath).ToArray());
        Assert.True(File.Exists(path), $"{path} no longer exists.");
        return File.ReadAllLines(path)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .ToList();
    }

    // ------------------------------------------------------------------ B-040

    /// <summary>
    /// B-040: <c>File.Exists</c> answers "no" for a file the caller may not read, so a permission
    /// problem was reported as <see cref="CredentialFileState.Missing"/> and the resolver silently
    /// fell through to another source. Only a genuinely absent file may report Missing.
    /// </summary>
    [Fact]
    public void Credential_read_does_not_use_File_Exists_to_decide_absence()
    {
        var source = string.Join(
            "\n",
            ExecutableLines("src", "server", "MusicHub.Infrastructure", "Security", "DatabaseCredentialFile.cs"));

        // File.Exists is still legitimate for the temp-file cleanup in Write. What must not come
        // back is the decision that turns a denial into Missing.
        Assert.DoesNotContain("if (!File.Exists(path))", source);
        Assert.Contains("UnauthorizedAccessException", source);
    }

    [Fact]
    public void Credential_read_reports_Missing_when_the_keys_directory_does_not_exist()
    {
        var provider = DataProtectionSetup.Create(_root);

        var credential = DatabaseCredentialFile.TryRead(_root, provider);

        Assert.Equal(CredentialFileState.Missing, credential.State);
        Assert.Null(credential.ConnectionString);
    }

    [Fact]
    public void Credential_read_reports_Missing_when_the_file_is_genuinely_absent()
    {
        // The keys directory exists (a keyring was created) but no credential was written: this is
        // the "not installed yet" state and must still fall back.
        var provider = DataProtectionSetup.Create(_root);
        Directory.CreateDirectory(Path.Combine(_root, DatabaseCredentialFile.DirectoryName));

        var credential = DatabaseCredentialFile.TryRead(_root, provider);

        Assert.Equal(CredentialFileState.Missing, credential.State);
    }

    [Fact]
    public void Credential_read_reports_Unreadable_when_the_path_is_a_directory()
    {
        // A file that exists but cannot be read as a file. File.Exists reports false for this,
        // which is exactly the conflation B-040 is about: it must not become Missing.
        var provider = DataProtectionSetup.Create(_root);
        Directory.CreateDirectory(
            Path.Combine(_root, DatabaseCredentialFile.DirectoryName, DatabaseCredentialFile.FileName));

        var credential = DatabaseCredentialFile.TryRead(_root, provider);

        Assert.Equal(CredentialFileState.Unreadable, credential.State);
    }

    [Fact]
    public void Credential_read_reports_Unreadable_when_the_file_is_locked_by_another_process()
    {
        // The installer rewrites the credential during a rotation; a host reading at that moment
        // must fail loudly rather than connect with a different password.
        var provider = DataProtectionSetup.Create(_root);
        var path = DatabaseCredentialFile.Write(
            _root,
            "Host=h;Port=5433;Database=d;Username=u;Password=UnreadableSecret123",
            provider);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var credential = DatabaseCredentialFile.TryRead(_root, provider);

            Assert.Equal(CredentialFileState.Unreadable, credential.State);
        }
    }

    [Fact]
    public void Resolve_never_falls_back_when_the_credential_file_cannot_be_read()
    {
        var provider = DataProtectionSetup.Create(_root);
        Directory.CreateDirectory(
            Path.Combine(_root, DatabaseCredentialFile.DirectoryName, DatabaseCredentialFile.FileName));

        // The environment override is consulted first, and ApiFactory sets it process-wide for the
        // integration collection. Without clearing it here this test passes in isolation and fails
        // in the full suite, which is exactly how the leak was found.
        var previous = Environment.GetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, null);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ConnectionStringResolver.Resolve(_root, "Host=fromconfig", provider, allowDevelopmentDefault: true));

            Assert.Contains("cannot be decrypted", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, previous);
        }
    }

    // ------------------------------------------------------------------ B-041

    /// <summary>
    /// B-041: the Worker hard-coded <c>allowDevelopmentDefault: true</c>, so a Production Worker
    /// with no credential file connected to the development default. The API used
    /// <c>IsDevelopment()</c>, so the two hosts disagreed about whether to fail loudly.
    /// </summary>
    [Theory]
    [InlineData("Development", true)]
    [InlineData("development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_Development_environment_may_fall_back_to_the_development_default(
        string? environmentName,
        bool expected)
    {
        Assert.Equal(expected, ConnectionStringResolver.AllowsDevelopmentDefault(environmentName));
    }

    [Fact]
    public void Production_resolution_with_nothing_configured_fails_loudly()
    {
        var provider = DataProtectionSetup.Create(_root);
        var previous = Environment.GetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, null);

            Assert.Throws<InvalidOperationException>(() =>
                ConnectionStringResolver.ResolveForHost("Production", _root, null, provider));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Development_resolution_with_nothing_configured_uses_the_development_default()
    {
        var provider = DataProtectionSetup.Create(_root);
        var previous = Environment.GetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, null);

            var resolved = ConnectionStringResolver.ResolveForHost("Development", _root, null, provider);

            Assert.Equal(ConnectionStringResolver.DevelopmentDefault, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Both_hosts_derive_the_fallback_from_the_same_shared_policy()
    {
        // The defect was that the Worker had its own answer. Assert the call site, because a
        // duplicated boolean cannot be caught by exercising either host in isolation.
        var worker = ExecutableLines("src", "server", "MusicHub.Worker", "Program.cs");

        Assert.Contains(worker, line => line.Contains("ResolveForHost", StringComparison.Ordinal));
        Assert.DoesNotContain(worker, line => line.Contains("allowDevelopmentDefault", StringComparison.Ordinal));
        // The raw development default must never be reachable from a host that did not ask for it.
        Assert.DoesNotContain(worker, line => line.Contains("DevelopmentDefault", StringComparison.Ordinal));
    }

    [Fact]
    public void The_api_also_uses_the_shared_policy()
    {
        var api = ExecutableLines("src", "server", "MusicHub.Api", "Program.cs");

        Assert.Contains(api, line => line.Contains("ResolveForHost", StringComparison.Ordinal));
        Assert.DoesNotContain(api, line => line.Contains("allowDevelopmentDefault", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ B-042

    /// <summary>
    /// B-042: PostgreSQL echoes the failing statement text in the error, so a statement containing
    /// the generated password printed it. Value-based scrubbing is the only reliable removal.
    /// </summary>
    [Theory]
    // 42601: the primary message quotes the statement fragment at the error position.
    [InlineData("42601: unterminated quoted string at or near \"'SecretValue123ABC\"\nPOSITION: 40")]
    // 42704: the password was embedded in an identifier.
    [InlineData("42704: role \"role SecretValue123ABC\" does not exist")]
    // CONTEXT lines carry the whole statement.
    [InlineData("ERROR: failure\nCONTEXT: SQL statement \"alter role \"x\" with login password 'SecretValue123ABC'\"")]
    public void A_known_secret_is_removed_wherever_the_server_echoed_it(string message)
    {
        const string secret = "SecretValue123ABC";

        var scrubbed = SecretRedactor.Scrub(message, secret);

        Assert.DoesNotContain(secret, scrubbed, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.Placeholder, scrubbed);
    }

    [Fact]
    public void Redaction_finds_no_credential_in_the_scrubbed_output()
    {
        // The end-to-end property: whatever the server wrote, the printed text must not contain it.
        const string password = "Kz9-Qw8_Er7-Ty6-Ui5-Op4-As3-Df2-Gh1-Jk0-Lm9-Nb8";
        var serverText =
            $"ERROR: 42601: unterminated quoted string at or near \"'{password}\"\n" +
            $"CONTEXT: SQL statement \"alter role \"musichub\" with login password '{password}'\"";

        var scrubbed = SecretRedactor.Scrub(serverText, password);

        Assert.DoesNotContain(password, scrubbed, StringComparison.Ordinal);
        // Line breaks are flattened so one secret-bearing line cannot look like two records.
        Assert.DoesNotContain('\n', scrubbed);
        Assert.DoesNotContain('\r', scrubbed);
    }

    [Fact]
    public void Redaction_still_handles_the_connection_string_form_when_the_value_is_unknown()
    {
        var scrubbed = SecretRedactor.ScrubPatterns(
            "Host=db;Port=5433;Username=musichub;Password=SomethingWeWereNotToldAbout");

        Assert.DoesNotContain("SomethingWeWereNotToldAbout", scrubbed, StringComparison.Ordinal);
        Assert.Contains("Password=***", scrubbed, StringComparison.Ordinal);
        Assert.Contains("Host=db", scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_of_a_pg_style_password_literal_works_without_knowing_the_value()
    {
        var scrubbed = SecretRedactor.ScrubPatterns(
            "SQL statement \"alter role \"x\" with login password 'unknown-value-here'\"");

        Assert.DoesNotContain("unknown-value-here", scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void Pattern_only_redaction_also_flattens_line_breaks()
    {
        // This entry point is used for output whose secrets are unknown; a multi-line server
        // message used to become several console lines and could look like extra records.
        var scrubbed = SecretRedactor.ScrubPatterns("first line\nPassword=leaked-value\nlast line");

        Assert.DoesNotContain("leaked-value", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', scrubbed);
    }

    [Fact]
    public void Short_values_are_left_alone_so_unrelated_text_is_not_shredded()
    {
        // Replacing a two-character "secret" would corrupt ordinary words without protecting
        // anything, so the literal pass ignores values below the minimum length.
        var scrubbed = SecretRedactor.Scrub("the role does not exist", "no");

        Assert.Equal("the role does not exist", scrubbed);
    }

    [Fact]
    public void Empty_input_still_produces_a_usable_message()
    {
        Assert.Equal("(no detail)", SecretRedactor.Scrub(null));
        Assert.Equal("(no detail)", SecretRedactor.Scrub(string.Empty));
        Assert.Equal("(no detail)", SecretRedactor.ScrubPatterns("   ".Trim()));
    }

    // ------------------------------------------------------------------ B-051

    /// <summary>
    /// B-051: the rotate path only set the password, so an existing superuser role stayed a
    /// superuser and its credential was written for the API. Both statements must now state every
    /// privileged attribute explicitly.
    /// </summary>
    [Fact]
    public void The_alter_statement_removes_every_elevated_privilege()
    {
        var statement = RoleProvisioning.BuildAlterRoleStatement("musichub", "pw");

        Assert.StartsWith("alter role \"musichub\" with ", statement, StringComparison.Ordinal);
        Assert.Contains("nosuperuser", statement, StringComparison.Ordinal);
        Assert.Contains("nocreatedb", statement, StringComparison.Ordinal);
        Assert.Contains("nocreaterole", statement, StringComparison.Ordinal);
        Assert.Contains("noreplication", statement, StringComparison.Ordinal);
        Assert.Contains("nobypassrls", statement, StringComparison.Ordinal);
        Assert.Contains("login", statement, StringComparison.Ordinal);
        // A statement that omitted LOGIN could lock the application out of its own database.
        Assert.DoesNotContain("nosuperuser superuser", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void The_create_statement_states_the_same_attributes()
    {
        var statement = RoleProvisioning.BuildCreateRoleStatement("musichub", "pw");

        Assert.StartsWith("create role \"musichub\" ", statement, StringComparison.Ordinal);
        foreach (var attribute in RoleProvisioning.ApplicationRoleAttributes.Split(' '))
        {
            Assert.Contains(attribute, statement, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_rotate_path_is_what_protects_an_existing_privileged_role()
    {
        // The regression was specifically in the "role already exists" branch, so the statement it
        // builds must be the hardening one, not a bare password change.
        var statement = RoleProvisioning.BuildAlterRoleStatement("legacy", "pw");

        Assert.Contains("nosuperuser", statement, StringComparison.Ordinal);
        Assert.Contains("password 'pw'", statement, StringComparison.Ordinal);

        var source = string.Join(
            "\n",
            ExecutableLines("src", "server", "MusicHub.Install", "Program.cs"));
        // The old inlined statement must not come back alongside the helper.
        Assert.DoesNotContain("alter role {QuoteIdentifier(username)} with login password", source);
        Assert.DoesNotContain("create role {QuoteIdentifier(username)} login password", source);
    }

    [Fact]
    public void An_elevated_role_is_refused_before_its_credential_could_be_written()
    {
        Assert.True(new RoleProvisioning.RolePrivileges(false, false, false, false, false).IsElevated == false);
        Assert.True(new RoleProvisioning.RolePrivileges(true, false, false, false, false).IsElevated);
        Assert.True(new RoleProvisioning.RolePrivileges(false, true, false, false, false).IsElevated);
        Assert.True(new RoleProvisioning.RolePrivileges(false, false, true, false, false).IsElevated);
        Assert.True(new RoleProvisioning.RolePrivileges(false, false, false, true, false).IsElevated);
        Assert.True(new RoleProvisioning.RolePrivileges(false, false, false, false, true).IsElevated);
    }

    [Fact]
    public void The_privilege_check_covers_inherited_superuser_membership()
    {
        // A role with rolsuper = false that is a member of a superuser role inherits superuser
        // rights; pg_has_role(..., 'usage') is true while the flag stays false. Asserting the
        // attributes alone would let such a role through, so the guard must also look at
        // pg_auth_members. Verified against PostgreSQL 17.6 before adding this test.
        var source = string.Join(
            "\n",
            ExecutableLines("src", "server", "MusicHub.Install", "RoleProvisioning.cs"));

        Assert.Contains("ReadSuperuserMembershipsAsync", source, StringComparison.Ordinal);
        Assert.Contains("pg_auth_members", source, StringComparison.Ordinal);
        Assert.Contains("rolsuper", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("musichub")]
    [InlineData("music_hub-01")]
    [InlineData("a")]
    public void Identifiers_are_quoted(string identifier)
    {
        Assert.Equal($"\"{identifier}\"", RoleProvisioning.QuoteIdentifier(identifier));
    }

    [Fact]
    public void An_identifier_containing_a_quote_cannot_break_out_of_its_quotes()
    {
        Assert.Equal("\"a\"\"b\"", RoleProvisioning.QuoteIdentifier("a\"b"));
    }

    [Fact]
    public void Only_long_alphanumeric_passwords_may_be_interpolated_into_sql()
    {
        // The generated password is 32 random bytes base64url-encoded, which is 43 characters,
        // so the 40-character floor sits just below anything the generator can produce.
        static string Of(int length, char fill = 'a') => "Kz9-" + new string(fill, length - 4);

        Assert.True(RoleProvisioning.IsSafeRolePassword(Of(40)));
        Assert.True(RoleProvisioning.IsSafeRolePassword(Of(64)));
        Assert.False(RoleProvisioning.IsSafeRolePassword(Of(39)));
        Assert.False(RoleProvisioning.IsSafeRolePassword("short"));

        // Characters that would break out of, or be re-interpreted inside, a SQL string literal.
        Assert.False(RoleProvisioning.IsSafeRolePassword(Of(40)[..39] + "'"));
        Assert.False(RoleProvisioning.IsSafeRolePassword(Of(40)[..39] + " "));
        Assert.False(RoleProvisioning.IsSafeRolePassword(Of(40)[..39] + "\\"));
        Assert.False(RoleProvisioning.IsSafeRolePassword(Of(40)[..39] + ";"));
        Assert.False(RoleProvisioning.IsSafeRolePassword(Of(40)[..39] + "\""));
    }

    // ------------------------------------------------------- Telegram operator CLI
    //
    // The installer gained `check-telegram-token` and `set-telegram-admin` so an operator can
    // validate a bot token without pasting it into curl. The whole point is that the token never
    // reaches a place it can be recovered from, so these tests assert the mechanisms that keep it
    // out of arguments and out of output - not merely that the command runs.

    /// <summary>
    /// A token must not be acceptable as a command-line argument. The help text says so, but a help
    /// text is not an enforcement point: without the guard the natural attempt
    /// <c>check-telegram-token --token 123:ABC</c> would work, putting the token in the shell
    /// history file and in the process list.
    /// </summary>
    [Fact]
    public async Task The_token_check_command_refuses_any_argument()
    {
        var code = await MusicHub.Install.Program.Main(["check-telegram-token", "--token", "123:NOT-A-REAL-TOKEN"]);

        // 3 is the guard's own code. 2 is "no token supplied", which is what the same call would
        // return without the guard because no environment variable is set - so asserting 2 would
        // pass either way and prove nothing.
        Assert.Equal(3, code);
    }

    /// <summary>
    /// The same guard must also reject the shape an operator is most likely to try first, where the
    /// token is positional rather than named.
    /// </summary>
    [Fact]
    public async Task The_token_check_command_refuses_a_positional_token()
    {
        Assert.Equal(3, await MusicHub.Install.Program.Main(["check-telegram-token", "123:NOT-A-REAL-TOKEN"]));
    }

    /// <summary>
    /// With no environment variable and no redirected stdin there is nothing to read, and the
    /// command must fail rather than block forever waiting for input it will never receive.
    /// </summary>
    [Fact]
    public async Task The_token_check_command_fails_when_no_token_is_supplied()
    {
        var previous = Environment.GetEnvironmentVariable(TelegramTokenCheck.TokenEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(TelegramTokenCheck.TokenEnvironmentVariable, null);

            // The test host is not an interactive console, so Console.IsInputRedirected is true and
            // an empty stdin yields no token - the same path a pipeline with no data takes.
            var code = await MusicHub.Install.Program.Main(["check-telegram-token"]);

            Assert.Equal(2, code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TelegramTokenCheck.TokenEnvironmentVariable, previous);
        }
    }

    /// <summary>
    /// <c>--admin-user-id</c> is the authorization boundary <c>TelegramIngestionService</c>
    /// compares every sender against, so a missing, non-numeric or non-positive value must be a
    /// usage error rather than a silent write of 0 (which would authorize nobody, or - worse, if
    /// the comparison were ever relaxed to "id != 0" - anybody).
    /// </summary>
    /// <param name="tail">Remaining arguments, space-separated. Empty means none.</param>
    [Theory]
    [InlineData("")]
    [InlineData("--admin-user-id")]
    [InlineData("--admin-user-id abc")]
    [InlineData("--admin-user-id 0")]
    [InlineData("--admin-user-id -5")]
    public async Task Set_telegram_admin_rejects_an_invalid_user_id(string tail)
    {
        var args = new[] { "set-telegram-admin" }
            .Concat(tail.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        // Usage errors exit 1; SetAdminAsync's own positive-id check exits 2. Both are refusals, so
        // the assertion is "did not succeed" - the important part is that nothing is written.
        Assert.NotEqual(0, await MusicHub.Install.Program.Main(args));
    }

    /// <summary>
    /// A non-numeric chat id must be rejected. It is a separate option with its own parse, so it
    /// needs its own case rather than relying on the user-id guard.
    /// </summary>
    [Fact]
    public async Task Set_telegram_admin_rejects_a_non_numeric_chat_id()
    {
        var code = await MusicHub.Install.Program.Main(
            ["set-telegram-admin", "--admin-user-id", "123456789", "--admin-chat-id", "not-a-number"]);

        Assert.NotEqual(0, code);
    }

    /// <summary>
    /// The statement must be scoped to exactly one row.
    ///
    /// <para>
    /// <c>TelegramConfigurations</c> has only a primary key - no singleton constraint - and the API
    /// resolves the effective row with <c>OrderBy(UpdatedAt).FirstOrDefault()</c>. An unscoped
    /// <c>UPDATE</c> therefore appears to succeed while leaving the row the application actually
    /// reads untouched, and the bot silently keeps rejecting messages. This was observed live: the
    /// first version of this command updated a different row than the one the API would use.
    /// </para>
    /// </summary>
    [Fact]
    public void The_admin_update_targets_a_single_row_in_the_same_way_the_api_resolves_it()
    {
        var sql = TelegramTokenCheck.SetAdminSql;

        // A WHERE clause is what prevents the write-everything failure.
        Assert.Contains("where", sql, StringComparison.OrdinalIgnoreCase);

        // And it must pin the row by primary key, selecting the same ordering the API uses.
        Assert.Contains("\"Id\" =", sql, StringComparison.Ordinal);
        Assert.Contains("order by \"UpdatedAt\"", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limit 1", sql, StringComparison.OrdinalIgnoreCase);

        // Sanity: the parameters the caller binds are the ones this statement expects.
        Assert.Contains("$1", sql, StringComparison.Ordinal);
        Assert.Contains("$2", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// The read that decides which row is "the" configuration must agree with the API's ordering.
    /// If either side changes, the installer command silently starts writing a row nothing reads.
    /// </summary>
    [Fact]
    public void The_api_and_the_cli_order_the_configuration_row_the_same_way()
    {
        var api = ExecutableLines("src", "server", "MusicHub.Api", "Endpoints", "TelegramEndpoints.cs");
        var apiReads = api
            .Where(line => line.Contains("TelegramConfigurations", StringComparison.Ordinal)
                        && line.Contains("OrderBy", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(apiReads);
        Assert.All(apiReads, line => Assert.Contains("x => x.UpdatedAt", line, StringComparison.Ordinal));

        Assert.Contains(
            "order by \"UpdatedAt\"",
            TelegramTokenCheck.SetAdminSql,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The reason <c>TelegramTokenCheck</c> must scrub by value rather than by pattern: a bot token
    /// lives inside the request URL, and a transport failure echoes that URL back. The pattern
    /// redactor knows <c>password=value</c> shapes only, so it leaves a bare token untouched.
    /// </summary>
    [Fact]
    public void Pattern_only_redaction_would_leak_a_token_embedded_in_a_url()
    {
        // Obviously synthetic. It must never be a real credential: this file is committed.
        const string token = "1234567890:AAHfake-token-value-for-redaction-tests-only";
        var message = $"An error occurred while sending the request to https://api.telegram.org/bot{token}/getMe";

        // The starting point: the token really is present in the text being scrubbed.
        Assert.Contains(token, message, StringComparison.Ordinal);

        // Pattern-only scrubbing cannot recognise it - this is the leak the by-value call prevents.
        Assert.Contains(token, SecretRedactor.ScrubPatterns(message), StringComparison.Ordinal);

        // By-value scrubbing removes it completely.
        Assert.DoesNotContain(token, SecretRedactor.Scrub(message, token), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every error path in the helper must therefore scrub with the known secret. This is asserted
    /// against the source because the alternative - calling the live Telegram API from a unit test -
    /// would contact a real bot, and no fake is allowed on a production path.
    /// </summary>
    [Fact]
    public void The_token_check_scrubs_error_text_with_the_known_secret()
    {
        var lines = ExecutableLines("src", "server", "MusicHub.Install", "TelegramTokenCheck.cs");

        var errorWrites = lines
            .Where(line => line.Contains("Console.Error.WriteLine", StringComparison.Ordinal))
            .Where(line => line.Contains("ex.Message", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(errorWrites);

        // Scrub(text, secret) - two arguments. ScrubPatterns(text) is not acceptable here: it
        // cannot see a bare token inside a URL, as the test above demonstrates.
        Assert.All(errorWrites, line => Assert.Contains("Scrub(", line, StringComparison.Ordinal));
        Assert.All(errorWrites, line => Assert.DoesNotContain("ScrubPatterns(", line, StringComparison.Ordinal));

        // And no console write anywhere may interpolate the token itself.
        var holes = new System.Text.RegularExpressions.Regex(@"\{[^{}]*\btoken\b[^{}]*\}");
        var offenders = lines
            .Where(line => line.Contains("Console.", StringComparison.Ordinal)
                        || line.Contains("throw new", StringComparison.Ordinal))
            .Where(line => holes.IsMatch(line))
            .Where(line => !line.Contains("Scrub(", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Console output or an exception message interpolates the token without scrubbing it: "
            + string.Join(" | ", offenders));
    }

    /// <summary>
    /// The helper must stay read-only. Sending a message would be visible to real Telegram users
    /// and could not be undone, so only the two harmless calls may be used.
    /// </summary>
    [Fact]
    public void The_token_check_uses_only_the_read_only_telegram_calls()
    {
        var lines = ExecutableLines("src", "server", "MusicHub.Install", "TelegramTokenCheck.cs");
        var text = string.Join('\n', lines);

        Assert.Contains("GetMeAsync", text, StringComparison.Ordinal);
        Assert.Contains("getWebhookInfo", text, StringComparison.Ordinal);

        // These all mutate Telegram state or contact users.
        Assert.DoesNotContain("SendMessageAsync", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SendAudioAsync", text, StringComparison.Ordinal);
        Assert.DoesNotContain("setWebhook", text, StringComparison.Ordinal);
        Assert.DoesNotContain("deleteWebhook", text, StringComparison.Ordinal);
        Assert.DoesNotContain("getUpdates", text, StringComparison.Ordinal);
    }
}
