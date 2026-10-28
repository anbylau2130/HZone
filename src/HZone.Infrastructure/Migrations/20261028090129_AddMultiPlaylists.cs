using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HZone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiPlaylists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlaylistItems_UserId_SortOrder",
                table: "PlaylistItems");

            migrationBuilder.AddColumn<Guid>(
                name: "PlaylistId",
                table: "PlaylistItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_PlaylistId_SortOrder",
                table: "PlaylistItems",
                columns: new[] { "PlaylistId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_UserId_CreatedAt",
                table: "Playlists",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_PlaylistItems_Playlists_PlaylistId",
                table: "PlaylistItems",
                column: "PlaylistId",
                principalTable: "Playlists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 存量回填：已有列表项的用户每人一张「默认歌单」（CreatedAt 压到最小保持排前），
            // 全部既有条目挂靠其上；无条目用户不建（GET /me/playlists 惰性兜底）。
            migrationBuilder.Sql("""
                INSERT INTO Playlists (Id, UserId, Name, CreatedAt)
                SELECT
                    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
                        substr(hex(randomblob(2)), 2) || '-' ||
                        substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) ||
                        '-' || hex(randomblob(6))),
                    UserId,
                    '默认歌单',
                    '2000-01-01 00:00:00'
                FROM (SELECT DISTINCT UserId FROM PlaylistItems);

                UPDATE PlaylistItems
                SET PlaylistId = (SELECT p.Id FROM Playlists p WHERE p.UserId = PlaylistItems.UserId)
                WHERE PlaylistId IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlaylistItems_Playlists_PlaylistId",
                table: "PlaylistItems");

            migrationBuilder.DropTable(
                name: "Playlists");

            migrationBuilder.DropIndex(
                name: "IX_PlaylistItems_PlaylistId_SortOrder",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "PlaylistId",
                table: "PlaylistItems");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_UserId_SortOrder",
                table: "PlaylistItems",
                columns: new[] { "UserId", "SortOrder" });
        }
    }
}
