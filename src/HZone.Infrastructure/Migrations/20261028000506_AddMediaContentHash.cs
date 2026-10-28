using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HZone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "MediaAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_OwnerUserId_ContentHash",
                table: "MediaAssets",
                columns: new[] { "OwnerUserId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_OwnerUserId_ContentHash",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "MediaAssets");
        }
    }
}
