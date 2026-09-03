using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReadyRespondRiskActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalCount",
                table: "GitHubPullRequestStates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckState",
                table: "GitHubPullRequestStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasChangesRequested",
                table: "GitHubPullRequestStates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasConflicts",
                table: "GitHubPullRequestStates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMergeable",
                table: "GitHubPullRequestStates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOpen",
                table: "GitHubPullRequestStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadinessCheckedAt",
                table: "GitHubPullRequestStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAtRisk",
                table: "Actions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RiskReason",
                table: "Actions",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GitHubResponseStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GitHubAssigneeId = table.Column<long>(type: "INTEGER", nullable: false),
                    IsPending = table.Column<bool>(type: "INTEGER", nullable: false),
                    TriggerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastTriggerCommentId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastTriggeredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubResponseStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubResponseStates_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Actions_State_IsAtRisk_LastActivityAt",
                table: "Actions",
                columns: new[] { "State", "IsAtRisk", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubResponseStates_RepositoryId_SubjectType_SubjectNumber_GitHubAssigneeId",
                table: "GitHubResponseStates",
                columns: new[] { "RepositoryId", "SubjectType", "SubjectNumber", "GitHubAssigneeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubResponseStates");

            migrationBuilder.DropIndex(
                name: "IX_Actions_State_IsAtRisk_LastActivityAt",
                table: "Actions");

            migrationBuilder.DropColumn(
                name: "ApprovalCount",
                table: "GitHubPullRequestStates");

            migrationBuilder.DropColumn(
                name: "CheckState",
                table: "GitHubPullRequestStates");

            migrationBuilder.DropColumn(
                name: "HasChangesRequested",
                table: "GitHubPullRequestStates");

            migrationBuilder.DropColumn(
                name: "HasConflicts",
                table: "GitHubPullRequestStates");

            migrationBuilder.DropColumn(
                name: "IsMergeable",
                table: "GitHubPullRequestStates");

            migrationBuilder.DropColumn(
                name: "IsOpen",
                table: "GitHubPullRequestStates");

            migrationBuilder.DropColumn(
                name: "ReadinessCheckedAt",
                table: "GitHubPullRequestStates");

            migrationBuilder.DropColumn(
                name: "IsAtRisk",
                table: "Actions");

            migrationBuilder.DropColumn(
                name: "RiskReason",
                table: "Actions");
        }
    }
}
