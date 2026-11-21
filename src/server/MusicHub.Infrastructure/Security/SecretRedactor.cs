using System.Text.RegularExpressions;

namespace MusicHub.Infrastructure.Security;

/// <summary>
/// Removes secret material from text that is about to be printed or thrown.
///
/// <para>
/// Two independent layers are needed, because they catch different leaks:
/// </para>
/// <list type="number">
///   <item><description>
///   <b>Known secrets.</b> Callers that hold a secret pass it in and it is replaced
///   verbatim. This is the only layer that removes a value from arbitrary surrounding text,
///   which is what PostgreSQL does: a failing utility statement is echoed back inside
///   <c>ERROR</c> and <c>CONTEXT</c> lines, e.g.
///   <c>CONTEXT: SQL statement "alter role "x" with login password 'the-password'"</c>.
///   A pattern-based redactor cannot recognise that shape reliably (B-042).
///   </description></item>
///   <item><description>
///   <b>Secret-shaped patterns.</b> <c>password=value</c> / <c>pwd: value</c> ties, and the
///   <c>password 'value'</c> form PostgreSQL uses, are redacted even when the caller does not
///   know the value, because connection-string echoes are a recurring source of leaks.
///   </description></item>
/// </list>
/// <para>
/// Replacement is by literal value, not by regex, so a secret containing regex metacharacters
/// is still removed completely.
/// </para>
/// </summary>
public static class SecretRedactor
{
    /// <summary>Replacement marker. Deliberately fixed so output stays diffable.</summary>
    public const string Placeholder = "***";

    /// <summary>
    /// Secrets shorter than this are not value-replaced: replacing a 3-character string would
    /// shred unrelated text into noise without protecting anything meaningful.
    /// </summary>
    public const int MinimumSecretLength = 8;

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// <c>password=value</c>, <c>pwd = value</c>, <c>Password: value</c> and the PostgreSQL
    /// <c>password 'value'</c> form.
    /// </summary>
    private static readonly Regex SecretPattern = new(
        "(?i)(?<key>password|pwd)\\s*(?:=|:)\\s*[^;\\s\"']+" +
        "|(?<key2>password)\\s+'[^']*'",
        RegexOptions.Compiled,
        PatternTimeout);

    /// <summary>
    /// Scrub <paramref name="text"/> using secrets whose values are known to the caller.
    /// </summary>
    /// <param name="text">Text about to be printed, logged or thrown. May be null or empty.</param>
    /// <param name="secrets">
    /// Values to remove. Null, empty and short entries are ignored so a caller can pass an
    /// optional value without branching.
    /// </param>
    public static string Scrub(string? text, params string?[]? secrets)
    {
        if (string.IsNullOrEmpty(text)) return "(no detail)";

        var scrubbed = text;

        if (secrets is not null)
        {
            foreach (var secret in secrets)
            {
                if (string.IsNullOrEmpty(secret) || secret.Length < MinimumSecretLength) continue;

                // Ordinal: the exact bytes we generated are what the server echoes back.
                scrubbed = scrubbed.Replace(secret, Placeholder, StringComparison.Ordinal);
            }
        }

        // Patterns run last so a value that survived the literal pass is still covered.
        scrubbed = ScrubPatterns(scrubbed);

        return Flatten(scrubbed);
    }

    /// <summary>
    /// Pattern-only scrubbing for text whose secrets are not known, such as a message built by
    /// a library from a connection string. Safe to call on any string.
    /// </summary>
    public static string ScrubPatterns(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "(no detail)";

        try
        {
            var scrubbed = SecretPattern.Replace(
                text,
                match => match.Groups["key2"].Success
                    ? $"{match.Groups["key2"].Value} {Placeholder}"
                    : $"{match.Groups["key"].Value}{SeparatorOf(match.Value)}{Placeholder}");

            // Flattened here as well, because this is the entry point used for output whose
            // secrets are unknown: a multi-line message must not render as several console lines.
            return Flatten(scrubbed);
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail closed: if the pattern engine cannot decide, drop the whole message rather
            // than risk printing an unredacted line.
            return "(detail withheld)";
        }
    }

    /// <summary>Collapse newlines so one secret-bearing line cannot masquerade as several.</summary>
    private static string Flatten(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    private static string SeparatorOf(string matchedValue)
    {
        var index = matchedValue.IndexOfAny(['=', ':']);
        return index >= 0 ? matchedValue[index].ToString() : "=";
    }
}
