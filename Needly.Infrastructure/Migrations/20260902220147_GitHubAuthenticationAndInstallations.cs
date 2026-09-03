using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GitHubAuthenticationAndInstallations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountType",
                table: "Installations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "Installations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "UserInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitHubInstallationId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserInstallations_NeedlyUsers_NeedlyUserId",
                        column: x => x.NeedlyUserId,
                        principalTable: "NeedlyUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserInstallations_NeedlyUserId_GitHubInstallationId",
                table: "UserInstallations",
                columns: new[] { "NeedlyUserId", "GitHubInstallationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserInstallations");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "Installations");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Installations");
        }
    }
}
