using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GitHubActionDetectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubCheckFailureStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    HeadSha = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CheckKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    IsFailing = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubCheckFailureStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubCheckFailureStates_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GitHubPullRequestStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorGitHubUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    AuthorLogin = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    HeadSha = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    IsDraft = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubPullRequestStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubPullRequestStates_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GitHubReviewerFeedbackStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewerGitHubUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReviewerLogin = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ReviewId = table.Column<long>(type: "INTEGER", nullable: false),
                    HasOutstandingChanges = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApproximateUnresolvedCommentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubReviewerFeedbackStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubReviewerFeedbackStates_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GitHubReviewRequestStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AssigneeType = table.Column<int>(type: "INTEGER", nullable: false),
                    GitHubAssigneeId = table.Column<long>(type: "INTEGER", nullable: false),
                    AssigneeLogin = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubReviewRequestStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubReviewRequestStates_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubCheckFailureStates_RepositoryId_PullRequestNumber_HeadSha_CheckKey",
                table: "GitHubCheckFailureStates",
                columns: new[] { "RepositoryId", "PullRequestNumber", "HeadSha", "CheckKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubPullRequestStates_RepositoryId_PullRequestNumber",
                table: "GitHubPullRequestStates",
                columns: new[] { "RepositoryId", "PullRequestNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubReviewerFeedbackStates_RepositoryId_PullRequestNumber_ReviewerGitHubUserId",
                table: "GitHubReviewerFeedbackStates",
                columns: new[] { "RepositoryId", "PullRequestNumber", "ReviewerGitHubUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubReviewRequestStates_RepositoryId_PullRequestNumber_AssigneeType_GitHubAssigneeId",
                table: "GitHubReviewRequestStates",
                columns: new[] { "RepositoryId", "PullRequestNumber", "AssigneeType", "GitHubAssigneeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubCheckFailureStates");

            migrationBuilder.DropTable(
                name: "GitHubPullRequestStates");

            migrationBuilder.DropTable(
                name: "GitHubReviewerFeedbackStates");

            migrationBuilder.DropTable(
                name: "GitHubReviewRequestStates");
        }
    }
}
