using MusicHub.Infrastructure.Security;
using Npgsql;

namespace MusicHub.Install;

/// <summary>
/// The one place that decides what privileges the MusicHub application role gets.
///
/// <para>
/// This is separate from <see cref="Program"/> so the exact statements the installer runs can be
/// asserted by tests. Two defects lived in the copy that used to be inlined:
/// </para>
/// <list type="bullet">
///   <item><description>
///   The rotate path only set the password, so an existing superuser role stayed a superuser and
///   its credential was written for the API (B-051).
///   </description></item>
///   <item><description>
///   A failing statement carried the generated password into the console, because PostgreSQL
///   echoes the statement in its error <c>CONTEXT</c> and the old redaction only knew
///   <c>password=value</c> (B-042).
///   </description></item>
/// </list>
/// </summary>
public static class RoleProvisioning
{
    /// <summary>
    /// Every attribute is stated explicitly instead of inherited from the server default, so the
    /// result is identical whether the role is created or altered. <c>NOSUPERUSER</c> is the
    /// critical one; the others close the escalation paths a superuser implied.
    /// </summary>
    public const string ApplicationRoleAttributes =
        "login nosuperuser nocreatedb nocreaterole noreplication nobypassrls";

    /// <summary>Quote an identifier for PostgreSQL.</summary>
    public static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Statement for a role that does not exist yet. PostgreSQL's <c>CREATE ROLE ... LOGIN</c>
    /// already defaults to <c>NOSUPERUSER</c>, but it is stated so the text matches
    /// <see cref="BuildAlterRoleStatement"/> and a reviewer can see the intent.
    /// </summary>
    public static string BuildCreateRoleStatement(string username, string password) =>
        $"create role {QuoteIdentifier(username)} {ApplicationRoleAttributes} password '{password}'";

    /// <summary>
    /// Statement for an existing role: rotate the password <b>and</b> take away any privilege the
    /// role may have accumulated. Previously only the password was set (B-051).
    /// </summary>
    public static string BuildAlterRoleStatement(string username, string password) =>
        $"alter role {QuoteIdentifier(username)} with {ApplicationRoleAttributes} password '{password}'";

    /// <summary>True when the password may be interpolated into a SQL literal.</summary>
    public static bool IsSafeRolePassword(string password) =>
        password.Length >= 40 &&
        password.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    /// <summary>
    /// Run a statement that contains the generated password, guaranteeing the password cannot
    /// reach the console through the exception.
    ///
    /// <para>
    /// The original exception is never rethrown: its message and inner chain carry the server's
    /// echo of the statement.
    /// </para>
    /// </summary>
    public static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string commandText,
        string password,
        string failurePrefix)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            throw new InstallFailureException(
                $"{failurePrefix}: {SecretRedactor.Scrub(ex.Message, password)}");
        }
    }

    /// <summary>Privileges that make a role unsuitable as the application role.</summary>
    public readonly record struct RolePrivileges(
        bool Superuser,
        bool CreateDatabase,
        bool CreateRole,
        bool Replication,
        bool BypassRowLevelSecurity)
    {
        public bool IsElevated => Superuser || CreateDatabase || CreateRole || Replication || BypassRowLevelSecurity;
    }

    /// <summary>Read the role's privileges from the server.</summary>
    public static async Task<RolePrivileges> ReadPrivilegesAsync(NpgsqlConnection connection, string username)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "select rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls " +
            "from pg_roles where rolname = $1";
        command.Parameters.AddWithValue(username);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InstallFailureException(
                $"The PostgreSQL role '{username}' disappeared while it was being configured.");
        }

        return new RolePrivileges(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4));
    }

    /// <summary>
    /// Refuse to continue when the role is not a plain application role. Called before the
    /// credential file is written, so a privileged role can never become a stored credential.
    /// </summary>
    public static async Task AssertUnprivilegedAsync(NpgsqlConnection connection, string username)
    {
        var privileges = await ReadPrivilegesAsync(connection, username);
        if (privileges.IsElevated)
        {
            throw new InstallFailureException(
                $"Refusing to store a credential for the PostgreSQL role '{username}': it still has " +
                $"elevated privileges (superuser={privileges.Superuser}, createdb={privileges.CreateDatabase}, " +
                $"createrole={privileges.CreateRole}, replication={privileges.Replication}, " +
                $"bypassrls={privileges.BypassRowLevelSecurity}). The installer revokes them with ALTER ROLE, " +
                "so this means the server rejected the change. Use a dedicated application role; the API " +
                "must not run with these rights.");
        }

        var superuserMemberships = await ReadSuperuserMembershipsAsync(connection, username);
        if (superuserMemberships.Count > 0)
        {
            throw new InstallFailureException(
                $"Refusing to store a credential for the PostgreSQL role '{username}': it is a member of " +
                $"{string.Join(", ", superuserMemberships.Select(name => $"'{name}'"))} " +
                $"({(superuserMemberships.Count == 1 ? "a role that is" : "roles that are")} superuser). " +
                "A role's own rolsuper flag is false in that case, but the rights are inherited, so the API " +
                "would still run as a cluster superuser. Revoke the membership or use a dedicated role.");
        }
    }

    /// <summary>
    /// Names of the superuser roles <paramref name="username"/> is a member of.
    ///
    /// <para>
    /// <c>rolsuper</c> alone is not enough to prove a role is unprivileged (B-051). A role with
    /// <c>rolsuper = false</c> that is a member of a superuser role inherits those rights:
    /// <c>pg_has_role(..., 'usage')</c> returns true and the role can act as a superuser without
    /// ever showing the flag. Measured on PostgreSQL 17.6, which is why this check exists next to
    /// the attribute check instead of relying on it.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<string>> ReadSuperuserMembershipsAsync(
        NpgsqlConnection connection,
        string username)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "select g.rolname " +
            "from pg_auth_members m " +
            "join pg_roles g on g.oid = m.roleid " +
            "join pg_roles u on u.oid = m.member " +
            "where u.rolname = $1 and g.rolsuper " +
            "order by g.rolname";
        command.Parameters.AddWithValue(username);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }
}

/// <summary>
/// An operational failure whose message is already safe to print: any secret it quoted has been
/// removed by <see cref="SecretRedactor"/>. Distinguished from a usage error only for the exit code.
/// </summary>
public sealed class InstallFailureException(string message) : Exception(message);
