using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCanUsePlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanUsePlayer",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanUsePlayer",
                table: "Users");
        }
    }
}
