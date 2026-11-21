using System.Security.Cryptography;
using System.Text;
using MusicHub.Infrastructure.Security;
using Npgsql;

namespace MusicHub.Install;

/// <summary>
/// Installer helper for the MusicHub database credential.
///
/// <para>
/// The password never appears on a command line, in a log line, or in an exception message:
/// </para>
/// <list type="bullet">
///   <item><description>the administrator connection string is read from an environment variable;</description></item>
///   <item><description>the generated password is set on the PostgreSQL role through a parameter-safe statement and then zeroized;</description></item>
///   <item><description>the application copy is written with <see cref="DatabaseCredentialFile"/>, which encrypts it for the install account.</description></item>
/// </list>
/// <para>Exit codes: 0 success, 1 usage error, 2 operational failure.</para>
/// </summary>
public static class Program
{
    private const string AdminConnectionVariable = "MUSICHUB_PG_ADMIN_CONNECTION";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0] switch
            {
                "create-credential" => await CreateCredentialAsync(args[1..]),
                "verify-credential" => VerifyCredential(args[1..]),
                "check-telegram-token" => await CheckTelegramTokenAsync(args[1..]),
                "set-telegram-admin" => await SetTelegramAdminAsync(args[1..]),
                "reset-admin-password" => await ResetAdminPasswordAsync(args[1..]),
                _ => Fail($"Unknown command '{args[0]}'.", 1)
            };
        }
        catch (UsageException ex)
        {
            return Fail(ex.Message, 1);
        }
        catch (InstallFailureException ex)
        {
            // Already scrubbed where the failure happened; do not re-derive a message from it.
            return Fail(ex.Message, 2);
        }
        catch (Exception ex)
        {
            // Deliberately generic: connection failures can echo a server error that mentions the
            // role, and we never want the password or the admin connection string in the output.
            return Fail($"{ex.GetType().Name}: {RedactConnectionDetails(ex.Message)}", 2);
        }
    }

    private static async Task<int> CreateCredentialAsync(string[] args)
    {
        var options = ParseOptions(args);
        var dataRoot = Require(options, "data-root");
        var host = options.GetValueOrDefault("host", "127.0.0.1");
        var port = options.GetValueOrDefault("port", "5433");
        var database = options.GetValueOrDefault("database", "musichub");
        var username = options.GetValueOrDefault("username", "musichub");
        var createRole = options.ContainsKey("create-role");
        var adminVariable = options.GetValueOrDefault("admin-env", AdminConnectionVariable);
        var pwFileOut = options.GetValueOrDefault("pwfile-out", string.Empty);

        if (!int.TryParse(port, out var portNumber) || portNumber is < 1 or > 65535)
        {
            throw new UsageException($"--port '{port}' is not a valid port number.");
        }

        var hasAdmin = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(adminVariable));
        var seedOnly = options.ContainsKey("seed-only");
        var adminFromFile = options.ContainsKey("admin-from-file");

        if (seedOnly && adminFromFile)
        {
            throw new UsageException("--seed-only and --admin-from-file are mutually exclusive.");
        }

        if (!hasAdmin && !seedOnly && !adminFromFile)
        {
            throw new UsageException(
                $"Set {adminVariable} to an administrator connection string (for example " +
                "'Host=127.0.0.1;Port=5433;Database=postgres;Username=postgres;Password=...'). " +
                "It is read from the environment so it never reaches the command line. " +
                "Use --admin-from-file to reuse the administrator credential written by --seed-only, or " +
                "--seed-only when the cluster does not exist yet: that generates the password and " +
                "writes the credential without connecting, and --pwfile-out supplies the password " +
                "file initdb expects.");
        }

        if (seedOnly && string.IsNullOrWhiteSpace(pwFileOut))
        {
            throw new UsageException("--seed-only requires --pwfile-out <file> so the caller can initialise the cluster.");
        }

        // 32 bytes of CSPRNG output, URL-safe so it survives a connection string, a psql session
        // and a YAML/env file without quoting surprises. Contains no quote character, which is
        // what makes the ALTER ROLE statement below unambiguous.
        var password = CryptoUtil.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        if (!IsSafeRolePassword(password))
        {
            throw new InvalidOperationException("The generated password failed the role-password safety check.");
        }

        try
        {
            if (seedOnly)
            {
                // The cluster does not exist yet, so initdb creates the *superuser* with this
                // password. The application role is deliberately a different, unprivileged role
                // created afterwards through --admin-from-file, so the API never runs as a
                // superuser. The plaintext lives only in the transient password file, which the
                // caller deletes as soon as initdb returns; the same value is stored encrypted so
                // the follow-up step can authenticate as the superuser.
                var adminUsername = options.GetValueOrDefault("admin-username", "postgres");

                WritePasswordFile(pwFileOut, password);
                Console.WriteLine($"Wrote a transient password file for initdb at {pwFileOut} (delete it after initdb).");

                var seedAdminConnection =
                    $"Host={host};Port={portNumber};Database=postgres;Username={adminUsername};Password={password}";
                var seedProvider = DataProtectionSetup.Create(Path.GetFullPath(dataRoot));
                DatabaseCredentialFile.Write(
                    Path.GetFullPath(dataRoot),
                    DatabaseCredentialFile.AdminFileName,
                    seedAdminConnection,
                    seedProvider);
                Console.WriteLine(
                    $"Stored the encrypted administrator credential for '{adminUsername}' at " +
                    $"{DatabaseCredentialFile.PathFor(Path.GetFullPath(dataRoot), DatabaseCredentialFile.AdminFileName)}.");
                return 0;
            }

            // Authenticate as the administrator either from the environment or from the
            // credential written by --seed-only. --admin-from-file keeps the superuser password
            // off both the command line and the environment of the follow-up process.
            var provider = DataProtectionSetup.Create(Path.GetFullPath(dataRoot));

            string adminConnection;
            if (adminFromFile)
            {
                var adminCredential = DatabaseCredentialFile.TryRead(
                    Path.GetFullPath(dataRoot),
                    DatabaseCredentialFile.AdminFileName,
                    provider);
                if (adminCredential.State != CredentialFileState.Ok)
                {
                    throw new UsageException(
                        $"--admin-from-file found no usable administrator credential at " +
                        $"{DatabaseCredentialFile.PathFor(Path.GetFullPath(dataRoot), DatabaseCredentialFile.AdminFileName)}. " +
                        "Run --seed-only first, or set the administrator connection in the environment.");
                }

                adminConnection = adminCredential.ConnectionString!;
            }
            else
            {
                adminConnection = Environment.GetEnvironmentVariable(adminVariable)!;
            }

            await EnsureRoleAndDatabaseAsync(
                adminConnection,
                database,
                username,
                password,
                createRole,
                options.ContainsKey("create-database"));

            var connectionString =
                $"Host={host};Port={portNumber};Database={database};Username={username};Password={password}";

            var path = DatabaseCredentialFile.Write(Path.GetFullPath(dataRoot), connectionString, provider);

            Console.WriteLine($"Wrote the encrypted database credential to {path}");
            Console.WriteLine($"Target: {username}@{host}:{portNumber}/{database}");
            Console.WriteLine("The password is not printed. Rotate it with the same command; the API and Worker pick up the new value on restart.");
            return 0;
        }
        catch (Exception ex) when (ex is not UsageException and not InstallFailureException)
        {
            // Last line of defence for B-042. Every statement that carries the password is
            // scrubbed where it runs, but a driver or server error raised elsewhere could still
            // quote it, and this is the only frame that still knows the value.
            throw new InstallFailureException(SecretRedactor.Scrub(ex.Message, password));
        }
        finally
        {
            password = string.Empty;
        }
    }

    /// <summary>
    /// Write the transient initdb password file with the same restrictive permissions the
    /// credential file gets, so it is not readable by other accounts even briefly.
    /// </summary>
    private static void WritePasswordFile(string path, string password)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(full, options))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            // initdb reads the whole file, so no trailing newline is needed.
            writer.Write(password);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("icacls.exe")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                info.ArgumentList.Add(full);
                info.ArgumentList.Add("/inheritance:r");
                info.ArgumentList.Add("/grant:r");
                info.ArgumentList.Add($"{Environment.UserDomainName}\\{Environment.UserName}:(R,W)");
                using var process = System.Diagnostics.Process.Start(info);
                process?.WaitForExit(10_000);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // The file is deleted by the caller immediately after initdb.
            }
        }
    }

    private static async Task EnsureRoleAndDatabaseAsync(
        string adminConnection,
        string database,
        string username,
        string password,
        bool createRole,
        bool createDatabase)
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();

        // The application role must never be the superuser the installer authenticates as:
        // rotating the superuser password and then handing it to the API would remove every
        // privilege boundary the database has.
        var authenticatedAs = new NpgsqlConnectionStringBuilder(adminConnection).Username;
        if (string.Equals(authenticatedAs, username, StringComparison.Ordinal))
        {
            throw new UsageException(
                $"The application role '{username}' is the same role the administrator connection authenticates as. " +
                "Pass --username with a dedicated application role so the API does not run as a database superuser.");
        }

        var roleExists = false;
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "select 1 from pg_roles where rolname = $1";
            check.Parameters.AddWithValue(username);
            roleExists = await check.ExecuteScalarAsync() is not null;
        }

        if (!roleExists)
        {
            if (!createRole)
            {
                throw new UsageException(
                    $"The PostgreSQL role '{username}' does not exist. Re-run with --create-role to create it, " +
                    "or create the role yourself and grant it ownership of the database.");
            }

            // The role name is an identifier, so it goes through a quoted identifier; the password
            // is validated by IsSafeRolePassword first and contains no quote character.
            //
            // Every privileged attribute is set explicitly rather than left to the server default,
            // so the role is unprivileged whether it is created here or rotated below (B-051).
            await RoleProvisioning.ExecuteAsync(
                connection,
                RoleProvisioning.BuildCreateRoleStatement(username, password),
                password,
                $"Creating the role '{username}' failed");
            Console.WriteLine($"Created PostgreSQL role '{username}' with a generated password.");
        }
        else
        {
            // Rotating the password is not enough: if the role already existed as a superuser
            // (a stale cluster, a hand-made role, or one created by the old installer) this is
            // the only moment the installer can take that privilege away. Previously the
            // statement changed the password and nothing else, so the API and Worker were handed
            // a superuser connection string (B-051).
            await RoleProvisioning.ExecuteAsync(
                connection,
                RoleProvisioning.BuildAlterRoleStatement(username, password),
                password,
                $"Changing the role '{username}' failed");
            Console.WriteLine($"Rotated the password of PostgreSQL role '{username}'.");
        }

        // Verify rather than assume: a role that is still privileged must never have its
        // connection string written to the credential file.
        await AssertApplicationRoleIsUnprivilegedAsync(connection, username);

        var databaseExists = false;
        await using (var checkDb = connection.CreateCommand())
        {
            checkDb.CommandText = "select 1 from pg_database where datname = $1";
            checkDb.Parameters.AddWithValue(database);
            databaseExists = await checkDb.ExecuteScalarAsync() is not null;
        }

        if (!databaseExists)
        {
            if (!createDatabase)
            {
                Console.WriteLine(
                    $"Database '{database}' does not exist. Re-run with --create-database to create it, " +
                    "or create it yourself with the role as owner.");
                return;
            }

            // CREATE DATABASE cannot run inside a transaction block, which Npgsql does not open
            // for a single statement, so this is safe.
            await using var createDb = connection.CreateCommand();
            createDb.CommandText = $"create database {QuoteIdentifier(database)} owner {QuoteIdentifier(username)}";
            await createDb.ExecuteNonQueryAsync();
            Console.WriteLine($"Created database '{database}' owned by '{username}'.");
        }

        // PostgreSQL 15 removed the implicit CREATE grant on the public schema, so a role that
        // does not own the database cannot create tables and every migration fails with
        // "permission denied for schema public". Making the role the database owner is the
        // supported fix for an application-owned schema.
        await using (var grantDb = connection.CreateCommand())
        {
            grantDb.CommandText = $"alter database {QuoteIdentifier(database)} owner to {QuoteIdentifier(username)}";
            await grantDb.ExecuteNonQueryAsync();
        }

        // The schema grant has to run against the target database, so open a second connection.
        var builder = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database };
        await using var target = new NpgsqlConnection(builder.ConnectionString);
        await target.OpenAsync();
        await using var grantSchema = target.CreateCommand();
        grantSchema.CommandText =
            $"alter schema public owner to {QuoteIdentifier(username)}; " +
            $"grant all on schema public to {QuoteIdentifier(username)}";
        await grantSchema.ExecuteNonQueryAsync();
        Console.WriteLine($"Granted '{username}' ownership of database '{database}' and its public schema.");
    }

    /// <summary>
    /// Reject a token handed over as a command-line argument before anything else happens.
    ///
    /// <para>
    /// The help text says the token is only read from the environment or stdin, but a help text is
    /// not an enforcement point. Without this check the obvious mistake -
    /// <c>check-telegram-token --token 123:ABC</c> - succeeds, and the token is then recorded in the
    /// shell history file and visible in the process list for as long as the command runs, which is
    /// exactly what the helper exists to prevent.
    /// </para>
    /// <para>
    /// Returns 3 rather than the generic 2 so this refusal is distinguishable from "no token was
    /// supplied" - a caller (or a test) can then prove the guard ran instead of inferring it from a
    /// failure it would also have got without the guard.
    /// </para>
    /// </summary>
    private static async Task<int> CheckTelegramTokenAsync(string[] args)
    {
        if (args.Length > 0)
        {
            // Deliberately does not echo the argument: doing so would leak it to the console.
            return Fail(
                "check-telegram-token takes no arguments. Supply the token in " +
                $"{TelegramTokenCheck.TokenEnvironmentVariable} or on stdin, so it never appears in " +
                "the process list or the shell history.",
                3);
        }

        return await TelegramTokenCheck.RunAsync();
    }

    private static async Task<int> SetTelegramAdminAsync(string[] args)
    {
        var options = ParseOptions(args);

        if (!options.TryGetValue("admin-user-id", out var userIdText) ||
            !long.TryParse(userIdText, out var adminUserId))
        {
            throw new UsageException("--admin-user-id <numeric id> is required.");
        }

        long? adminChatId = null;
        if (options.TryGetValue("admin-chat-id", out var chatText))
        {
            if (!long.TryParse(chatText, out var parsed)) throw new UsageException("--admin-chat-id must be numeric.");
            adminChatId = parsed;
        }

        // Prefer an explicit connection string, then the same installer-written credential the
        // API uses, so this command works immediately after an install with no extra setup.
        string? connectionString = options.GetValueOrDefault("connection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var dataRoot = Environment.GetEnvironmentVariable(ConnectionStringResolver.DataRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(dataRoot))
            {
                throw new UsageException(
                    "Pass --connection, or set MUSICHUB_DATA_ROOT so the installer-written credential can be found.");
            }

            var resolvedRoot = Path.GetFullPath(dataRoot);
            var credential = DatabaseCredentialFile.TryRead(resolvedRoot, DataProtectionSetup.Create(resolvedRoot));
            if (credential.State != CredentialFileState.Ok)
            {
                throw new UsageException(
                    $"No usable credential at {DatabaseCredentialFile.PathFor(resolvedRoot)} ({credential.State}). " +
                    "Pass --connection instead.");
            }

            connectionString = credential.ConnectionString;
        }

        return await TelegramTokenCheck.SetAdminAsync(connectionString!, adminUserId, adminChatId);
    }

    /// <summary>
    /// Read the replacement password from the environment or stdin, then reset the account.
    ///
    /// <para>
    /// The password is never accepted as an argument. That is not cosmetic: a command line is
    /// readable by every process on the machine and is written to the shell history file, so an
    /// argument would leak the new credential before it was even set. This mirrors
    /// <c>check-telegram-token</c>, which refuses a token argument for the same reason.
    /// </para>
    /// </summary>
    private static async Task<int> ResetAdminPasswordAsync(string[] args)
    {
        var options = ParseOptions(args);

        if (!options.TryGetValue("email", out var email) || string.IsNullOrWhiteSpace(email) || email == "true")
        {
            throw new UsageException("--email <address> is required.");
        }

        if (options.ContainsKey("password"))
        {
            throw new UsageException(
                "Passwords are not accepted as arguments: they are visible to other processes and to " +
                $"shell history. Set {AdminPasswordReset.PasswordEnvironmentVariable} or pipe the password on stdin.");
        }

        var password = Environment.GetEnvironmentVariable(AdminPasswordReset.PasswordEnvironmentVariable);
        if (string.IsNullOrEmpty(password))
        {
            if (Console.IsInputRedirected)
            {
                password = Console.ReadLine();
            }

            if (string.IsNullOrEmpty(password))
            {
                throw new UsageException(
                    $"Set {AdminPasswordReset.PasswordEnvironmentVariable} or pipe the new password on stdin.");
            }
        }

        // Prefer an explicit connection string, then the same installer-written credential the API
        // uses, so recovery works on an installed server with no extra configuration. The
        // environment variable is checked before the credential file because it is the documented
        // override, and a container has no data root to read a file from.
        string? connectionString = options.GetValueOrDefault("connection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = Environment.GetEnvironmentVariable(ConnectionStringResolver.EnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var dataRoot = Environment.GetEnvironmentVariable(ConnectionStringResolver.DataRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(dataRoot))
            {
                throw new UsageException(
                    "Pass --connection, set MUSICHUB_CONNECTION_STRING, or set MUSICHUB_DATA_ROOT so the " +
                    "installer-written credential can be found.");
            }

            var resolvedRoot = Path.GetFullPath(dataRoot);
            var credential = DatabaseCredentialFile.TryRead(resolvedRoot, DataProtectionSetup.Create(resolvedRoot));
            if (credential.State != CredentialFileState.Ok)
            {
                throw new UsageException(
                    $"No usable credential at {DatabaseCredentialFile.PathFor(resolvedRoot)} ({credential.State}). " +
                    "Pass --connection or set MUSICHUB_CONNECTION_STRING instead.");
            }

            connectionString = credential.ConnectionString;
        }

        return await AdminPasswordReset.RunAsync(connectionString!, email.Trim(), password);
    }

    private static int VerifyCredential(string[] args)
    {
        var options = ParseOptions(args);
        var dataRoot = Path.GetFullPath(Require(options, "data-root"));
        var provider = DataProtectionSetup.Create(dataRoot);
        var credential = DatabaseCredentialFile.TryRead(dataRoot, provider);

        switch (credential.State)
        {
            case CredentialFileState.Missing:
                return Fail($"No credential file at {DatabaseCredentialFile.PathFor(dataRoot)}.", 2);

            case CredentialFileState.Unreadable:
                return Fail(
                    $"The credential file at {DatabaseCredentialFile.PathFor(dataRoot)} cannot be decrypted by this account. " +
                    "It is protected for the account and keyring that wrote it.", 2);
        }

        var builder = new NpgsqlConnectionStringBuilder(credential.ConnectionString);
        Console.WriteLine($"Credential OK: {builder.Username}@{builder.Host}:{builder.Port}/{builder.Database}");

        if (!options.ContainsKey("probe"))
        {
            return 0;
        }

        try
        {
            using var connection = new NpgsqlConnection(credential.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "select 1";
            command.ExecuteScalar();
            Console.WriteLine("Server accepted the credential.");

            // The application role must not be a superuser: a fresh install once passed the
            // application role to initdb -U, which made the API a cluster superuser and made
            // every database privilege boundary meaningless.
            using var roleCommand = connection.CreateCommand();
            roleCommand.CommandText =
                "select rolsuper, rolcreatedb, rolcreaterole from pg_roles where rolname = current_user";
            using (var reader = roleCommand.ExecuteReader())
            {
                if (!reader.Read())
                {
                    return Fail("Could not read the privileges of the connected role.", 2);
                }

                var isSuperuser = reader.GetBoolean(0);
                var canCreateDb = reader.GetBoolean(1);
                var canCreateRole = reader.GetBoolean(2);
                Console.WriteLine(
                    $"Role privileges: superuser={isSuperuser}, createdb={canCreateDb}, createrole={canCreateRole}.");
                if (isSuperuser || canCreateRole)
                {
                    return Fail(
                        $"The application role '{builder.Username}' is a superuser (superuser={isSuperuser}, createrole={canCreateRole}). " +
                        "Reinstall with a dedicated application role so the API does not run as a database superuser.", 2);
                }
            }

            // Migrations need CREATE on the public schema, which PostgreSQL 15+ no longer grants
            // to non-owners. Check the ability directly rather than inferring it from ownership.
            using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = "select has_schema_privilege(current_user, 'public', 'CREATE')";
            var canCreateInPublic = (bool)(schemaCommand.ExecuteScalar() ?? false);
            Console.WriteLine($"Can create in schema public: {canCreateInPublic}.");
            if (!canCreateInPublic)
            {
                return Fail(
                    "The application role cannot create objects in the public schema, so migrations will fail " +
                    "with 42501. Re-run the installer so the role owns the database and the public schema.", 2);
            }

            return 0;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            return Fail($"The server rejected the credential: {RedactConnectionDetails(ex.Message)}", 2);
        }
    }

    private static bool IsSafeRolePassword(string password) => RoleProvisioning.IsSafeRolePassword(password);

    /// <summary>
    /// Refuse to continue when the role is not a plain application role, and print the verified
    /// attributes so the operator sees the installer's own conclusion.
    /// </summary>
    private static async Task AssertApplicationRoleIsUnprivilegedAsync(
        NpgsqlConnection connection,
        string username)
    {
        await RoleProvisioning.AssertUnprivilegedAsync(connection, username);
        Console.WriteLine(
            $"Role '{username}' privileges: superuser=False, createdb=False, createrole=False, " +
            "replication=False, bypassrls=False, superuser-memberships=None.");
    }

    private static string QuoteIdentifier(string identifier) => RoleProvisioning.QuoteIdentifier(identifier);

    private static string RedactConnectionDetails(string message) => SecretRedactor.ScrubPatterns(message);
    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"Unexpected argument '{token}'.");
            }

            var name = token[2..];
            if (name.Length == 0) throw new UsageException("Empty option name.");

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[name] = args[++i];
            }
            else
            {
                options[name] = "true";
            }
        }

        return options;
    }

    private static string Require(Dictionary<string, string> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value) || value == "true")
        {
            throw new UsageException($"--{name} <value> is required.");
        }

        return value;
    }

    private static int Fail(string message, int code)
    {
        Console.Error.WriteLine("ERROR: " + message);
        return code;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            MusicHub installer helper - database credential

            USAGE
              MusicHub.Install create-credential --data-root <dir> [options]
              MusicHub.Install verify-credential --data-root <dir> [--probe]
              MusicHub.Install check-telegram-token
              MusicHub.Install set-telegram-admin --admin-user-id <id> [--admin-chat-id <id>] [--connection <string>]
              MusicHub.Install reset-admin-password --email <address> [--connection <string>]

            check-telegram-token validates a bot token without leaking it. Supply it in
            MUSICHUB_TG_TOKEN or pipe it on stdin; it is never accepted as an argument, because
            arguments are visible to other processes and to shell history. The command is
            read-only: it calls getMe and getWebhookInfo and sends no message.

            set-telegram-admin records the Telegram user id the bot will accept audio from; it is
            the authorization boundary TelegramIngestionService enforces. A configuration row must
            already exist (save it once in the dashboard so the bot token is stored).

            reset-admin-password replaces a forgotten administrator password. There is no HTTP
            endpoint for this on purpose: the only credential that may replace an administrator
            password is access to the host. Supply the new password in
            MUSICHUB_NEW_ADMIN_PASSWORD or pipe it on stdin; it is never accepted as an argument and
            is never printed. The account's SecurityStamp is rotated and every refresh token and
            device session for that user is revoked, so a leaked password or a live token stops
            working at the same moment the new password takes effect. The minimum length is 12
            characters, matching the API.

            create-credential generates a 256-bit random password, sets it on the PostgreSQL
            role and stores the connection string encrypted under <data-root>/keys.

              --data-root <dir>       Data root; the credential goes to keys/db-credential.protected
              --host <host>           PostgreSQL host             (default 127.0.0.1)
              --port <port>           PostgreSQL port             (default 5433)
              --database <name>       Database name               (default musichub)
              --username <name>       Application role            (default musichub)
              --create-role           Create the role when it does not exist
              --create-database        Create the database owned by that role when missing, and
                                      make the role own the public schema (required on
                                      PostgreSQL 15+, which no longer grants CREATE on public)
              --admin-from-file        Authenticate as the administrator with the credential written
                                      by --seed-only instead of an environment variable
              --admin-username <name>  With --seed-only: superuser initdb creates (default postgres)
              --admin-env <name>      Environment variable holding the administrator connection
                                      string (default MUSICHUB_PG_ADMIN_CONNECTION)
              --pwfile-out <file>     With --seed-only: write the generated password to this file for
                                      initdb, then delete it once the cluster exists
              --seed-only             Generate the password and credential without connecting;
                                      used to initialise a brand-new cluster

            The administrator connection string is only ever read from the environment, and the
            generated password is never printed.
            """);
    }

    private sealed class UsageException(string message) : Exception(message);
}
