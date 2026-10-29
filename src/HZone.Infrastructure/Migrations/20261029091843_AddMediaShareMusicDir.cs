using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HZone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaShareMusicDir : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "MediaAssets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Subdir",
                table: "MediaAssets",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsShared",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "Subdir",
                table: "MediaAssets");
        }
    }
}
