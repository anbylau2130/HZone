using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HZone.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ledgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsShared = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ledgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecorderUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AmountCents = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EntryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Users_RecorderUserId",
                        column: x => x.RecorderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerMembers_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LedgerMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_LedgerId_EntryDate",
                table: "LedgerEntries",
                columns: new[] { "LedgerId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_RecorderUserId",
                table: "LedgerEntries",
                column: "RecorderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerMembers_LedgerId_UserId",
                table: "LedgerMembers",
                columns: new[] { "LedgerId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerMembers_UserId",
                table: "LedgerMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_OwnerUserId",
                table: "Ledgers",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "LedgerMembers");

            migrationBuilder.DropTable(
                name: "Ledgers");
        }
    }
}
