using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GitHubWebhooksAndMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RawEvents_InstallationId_ReceivedAt",
                table: "RawEvents");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Teams",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "InstallationId",
                table: "RawEvents",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "RawEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "GitHubInstallationId",
                table: "RawEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "GitHubRepositoryId",
                table: "RawEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "RawEvents",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "RawEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "RawEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE RawEvents
                SET GitHubInstallationId = (
                    SELECT GitHubInstallationId
                    FROM Installations
                    WHERE Installations.Id = RawEvents.InstallationId);

                UPDATE RawEvents
                SET GitHubRepositoryId = (
                    SELECT GitHubRepositoryId
                    FROM Repositories
                    WHERE Repositories.Id = RawEvents.RepositoryId)
                WHERE RepositoryId IS NOT NULL;

                UPDATE RawEvents
                SET Status = 2
                WHERE ProcessedAt IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "InstallationMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitHubUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationMembers_GitHubUsers_GitHubUserId",
                        column: x => x.GitHubUserId,
                        principalTable: "GitHubUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstallationMembers_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitHubUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMembers_GitHubUsers_GitHubUserId",
                        column: x => x.GitHubUserId,
                        principalTable: "GitHubUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_GitHubInstallationId_GitHubRepositoryId_ReceivedAt",
                table: "RawEvents",
                columns: new[] { "GitHubInstallationId", "GitHubRepositoryId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_InstallationId",
                table: "RawEvents",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_Status_NextAttemptAt",
                table: "RawEvents",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InstallationMembers_GitHubUserId_IsActive",
                table: "InstallationMembers",
                columns: new[] { "GitHubUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_InstallationMembers_InstallationId_GitHubUserId",
                table: "InstallationMembers",
                columns: new[] { "InstallationId", "GitHubUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_GitHubUserId_IsActive",
                table: "TeamMembers",
                columns: new[] { "GitHubUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_GitHubUserId",
                table: "TeamMembers",
                columns: new[] { "TeamId", "GitHubUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstallationMembers");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_RawEvents_GitHubInstallationId_GitHubRepositoryId_ReceivedAt",
                table: "RawEvents");

            migrationBuilder.DropIndex(
                name: "IX_RawEvents_InstallationId",
                table: "RawEvents");

            migrationBuilder.DropIndex(
                name: "IX_RawEvents_Status_NextAttemptAt",
                table: "RawEvents");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "RawEvents");

            migrationBuilder.DropColumn(
                name: "GitHubInstallationId",
                table: "RawEvents");

            migrationBuilder.DropColumn(
                name: "GitHubRepositoryId",
                table: "RawEvents");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "RawEvents");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "RawEvents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "RawEvents");

            migrationBuilder.AlterColumn<Guid>(
                name: "InstallationId",
                table: "RawEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_InstallationId_ReceivedAt",
                table: "RawEvents",
                columns: new[] { "InstallationId", "ReceivedAt" });
        }
    }
}
