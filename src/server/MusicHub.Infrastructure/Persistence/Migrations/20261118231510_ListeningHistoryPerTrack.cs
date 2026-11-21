using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ListeningHistoryPerTrack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ListeningHistory_TrackId_PlayedAt",
                table: "ListeningHistory");

            migrationBuilder.DropIndex(
                name: "IX_ListeningHistory_UserId_PlayedAt",
                table: "ListeningHistory");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Description",
                value: "Full system administration and the only role that may change library content");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 2,
                column: "Description",
                value: "Read-only library access; content changes require an administrator");

            // The unique index cannot be created while the duplicate rows this bug produced still
            // exist, so collapse them first: keep the lowest Id per (UserId, TrackId), sum the play
            // counts and keep the most recent timestamp. Without this the migration fails on any
            // database that already hit CR-003.
            //
            // Order matters: the aggregate UPDATE runs before the DELETE. Running the DELETE first
            // discards the duplicate rows and their PlayCount along with them, so the SUM that
            // follows only sees the surviving row and the counted plays are silently lost.
            migrationBuilder.Sql("""
                UPDATE "ListeningHistory" h
                SET "PlayCount" = agg."PlayCount",
                    "PlayedAt" = agg."PlayedAt"
                FROM (
                    SELECT "UserId", "TrackId", SUM("PlayCount")::int AS "PlayCount", MAX("PlayedAt") AS "PlayedAt"
                    FROM "ListeningHistory"
                    GROUP BY "UserId", "TrackId"
                ) agg
                WHERE h."UserId" = agg."UserId" AND h."TrackId" = agg."TrackId";

                DELETE FROM "ListeningHistory" h
                USING "ListeningHistory" keep
                WHERE h."UserId" = keep."UserId"
                  AND h."TrackId" = keep."TrackId"
                  AND h."Id" > keep."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningHistory_PlayedAt",
                table: "ListeningHistory",
                column: "PlayedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningHistory_UserId_TrackId",
                table: "ListeningHistory",
                columns: new[] { "UserId", "TrackId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ListeningHistory_PlayedAt",
                table: "ListeningHistory");

            migrationBuilder.DropIndex(
                name: "IX_ListeningHistory_UserId_TrackId",
                table: "ListeningHistory");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Description",
                value: "Full system administration");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 2,
                column: "Description",
                value: "Upload and manage own music contributions");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningHistory_TrackId_PlayedAt",
                table: "ListeningHistory",
                columns: new[] { "TrackId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningHistory_UserId_PlayedAt",
                table: "ListeningHistory",
                columns: new[] { "UserId", "PlayedAt" });
        }
    }
}
