using System.Text.Json;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>
/// Writes the audit row every state-changing bot action must leave.
/// </summary>
/// <remarks>
/// <para>
/// The HTTP endpoints use <c>AuditWriter</c> in the API project, which fills
/// <see cref="AuditLog.ActorUserId"/> from a <c>ClaimsPrincipal</c>. The bot has no principal: the
/// caller is a Telegram account, identified by a numeric id that is not a user-account <see
/// cref="Guid"/>. So <see cref="AuditLog.ActorUserId"/> stays null and the Telegram id is recorded
/// in <see cref="AuditLog.DetailsJson"/> with a stable <c>actor</c> marker. That keeps "was this a
/// bot action or a dashboard action?" answerable, which is what the audit trail is for.
/// </para>
/// <para>
/// This is a static helper rather than an injected service so both the bot handler and the
/// ingestion service can call it with the <c>DbContext</c> they already hold, without another
/// registration in the two host projects. It saves immediately, after the domain write has already
/// been committed, so a refused operation never leaves an audit row behind.
/// </para>
/// </remarks>
public static class TelegramAudit
{
    /// <summary>Marks a row as written by the Telegram surface rather than by a dashboard user.</summary>
    public const string ActorKind = "telegram";

    /// <summary>Records one bot-initiated change.</summary>
    public static async Task RecordAsync(
        MusicHubDbContext db,
        long telegramUserId,
        long chatId,
        string action,
        string entityType,
        string? entityId,
        object? details,
        CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = null,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                actor = ActorKind,
                telegramUserId,
                chatId,
                details
            }),
            IpAddress = null
        });

        await db.SaveChangesAsync(ct);
    }
}
