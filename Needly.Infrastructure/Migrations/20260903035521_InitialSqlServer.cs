using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                    Login = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AvatarUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Installations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubInstallationId = table.Column<long>(type: "bigint", nullable: false),
                    AccountLogin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GitHubAccountId = table.Column<long>(type: "bigint", nullable: true),
                    AccountType = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NeedlyUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeedlyUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NeedlyUsers_GitHubUsers_GitHubUserId",
                        column: x => x.GitHubUserId,
                        principalTable: "GitHubUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstallationMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubRepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    HistoricalBootstrapStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HistoricalBootstrapCompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Repositories_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubTeamId = table.Column<long>(type: "bigint", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FilterJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    Effect = table.Column<int>(type: "int", nullable: false),
                    SnoozeDuration = table.Column<TimeSpan>(type: "time", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationRules_NeedlyUsers_NeedlyUserId",
                        column: x => x.NeedlyUserId,
                        principalTable: "NeedlyUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedViews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FilterJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedViews_NeedlyUsers_NeedlyUserId",
                        column: x => x.NeedlyUserId,
                        principalTable: "NeedlyUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubInstallationId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "Actions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    AssigneeType = table.Column<int>(type: "int", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectType = table.Column<int>(type: "int", nullable: false),
                    SubjectNumber = table.Column<int>(type: "int", nullable: false),
                    SubjectUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Context = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WaitingSince = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SnoozedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsAtRisk = table.Column<bool>(type: "bit", nullable: false),
                    RiskReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AuthorLogin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HasBotInvolvement = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Actions_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Actions_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActionSuppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectType = table.Column<int>(type: "int", nullable: false),
                    SubjectNumber = table.Column<int>(type: "int", nullable: false),
                    AssigneeType = table.Column<int>(type: "int", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionSuppressions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionSuppressions_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActionSuppressions_NeedlyUsers_NeedlyUserId",
                        column: x => x.NeedlyUserId,
                        principalTable: "NeedlyUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActionSuppressions_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GitHubCheckFailureStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "int", nullable: false),
                    HeadSha = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CheckKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    IsFailing = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "int", nullable: false),
                    AuthorGitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorLogin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HeadSha = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    IsDraft = table.Column<bool>(type: "bit", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    ApprovalCount = table.Column<int>(type: "int", nullable: true),
                    HasChangesRequested = table.Column<bool>(type: "bit", nullable: true),
                    CheckState = table.Column<int>(type: "int", nullable: false),
                    IsMergeable = table.Column<bool>(type: "bit", nullable: true),
                    HasConflicts = table.Column<bool>(type: "bit", nullable: true),
                    ReadinessCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                name: "GitHubResponseStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectType = table.Column<int>(type: "int", nullable: false),
                    SubjectNumber = table.Column<int>(type: "int", nullable: false),
                    GitHubAssigneeId = table.Column<long>(type: "bigint", nullable: false),
                    IsPending = table.Column<bool>(type: "bit", nullable: false),
                    TriggerCount = table.Column<int>(type: "int", nullable: false),
                    LastTriggerCommentId = table.Column<long>(type: "bigint", nullable: false),
                    LastTriggeredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "GitHubReviewerFeedbackStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "int", nullable: false),
                    ReviewerGitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                    ReviewerLogin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReviewId = table.Column<long>(type: "bigint", nullable: false),
                    HasOutstandingChanges = table.Column<bool>(type: "bit", nullable: false),
                    ApproximateUnresolvedCommentCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "int", nullable: false),
                    AssigneeType = table.Column<int>(type: "int", nullable: false),
                    GitHubAssigneeId = table.Column<long>(type: "bigint", nullable: false),
                    AssigneeLogin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsRequested = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "RawEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GitHubInstallationId = table.Column<long>(type: "bigint", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GitHubRepositoryId = table.Column<long>(type: "bigint", nullable: true),
                    DeliveryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventAction = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawEvents_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawEvents_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "ActionDispositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    IsMuted = table.Column<bool>(type: "bit", nullable: false),
                    SnoozedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsFyi = table.Column<bool>(type: "bit", nullable: false),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionDispositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionDispositions_Actions_ActionId",
                        column: x => x.ActionId,
                        principalTable: "Actions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActionDispositions_NeedlyUsers_NeedlyUserId",
                        column: x => x.NeedlyUserId,
                        principalTable: "NeedlyUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuleExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Effect = table.Column<int>(type: "int", nullable: false),
                    RuleOrder = table.Column<int>(type: "int", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleExecutions_Actions_ActionId",
                        column: x => x.ActionId,
                        principalTable: "Actions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RuleExecutions_NeedlyUsers_NeedlyUserId",
                        column: x => x.NeedlyUserId,
                        principalTable: "NeedlyUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActionLifecycleUndos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousState = table.Column<int>(type: "int", nullable: false),
                    PreviousSnoozedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AppliedState = table.Column<int>(type: "int", nullable: false),
                    SuppressionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionLifecycleUndos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionLifecycleUndos_ActionSuppressions_SuppressionId",
                        column: x => x.SuppressionId,
                        principalTable: "ActionSuppressions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActionLifecycleUndos_Actions_ActionId",
                        column: x => x.ActionId,
                        principalTable: "Actions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActionLifecycleUndos_NeedlyUsers_NeedlyUserId",
                        column: x => x.NeedlyUserId,
                        principalTable: "NeedlyUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActionEventReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DetectorKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionEventReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionEventReceipts_RawEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "RawEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionDispositions_ActionId",
                table: "ActionDispositions",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionDispositions_NeedlyUserId_ActionId",
                table: "ActionDispositions",
                columns: new[] { "NeedlyUserId", "ActionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionDispositions_NeedlyUserId_IsArchived_IsMuted_SnoozedUntil_IsPinned",
                table: "ActionDispositions",
                columns: new[] { "NeedlyUserId", "IsArchived", "IsMuted", "SnoozedUntil", "IsPinned" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionEventReceipts_EventId_DetectorKey",
                table: "ActionEventReceipts",
                columns: new[] { "EventId", "DetectorKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionLifecycleUndos_ActionId",
                table: "ActionLifecycleUndos",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionLifecycleUndos_NeedlyUserId_CreatedAt",
                table: "ActionLifecycleUndos",
                columns: new[] { "NeedlyUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionLifecycleUndos_SuppressionId",
                table: "ActionLifecycleUndos",
                column: "SuppressionId");

            migrationBuilder.CreateIndex(
                name: "IX_Actions_InstallationId_AssigneeType_AssigneeId_State_UpdatedAt",
                table: "Actions",
                columns: new[] { "InstallationId", "AssigneeType", "AssigneeId", "State", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Actions_Key",
                table: "Actions",
                column: "Key",
                unique: true,
                filter: "[State] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_Actions_RepositoryId",
                table: "Actions",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Actions_State_IsAtRisk_LastActivityAt",
                table: "Actions",
                columns: new[] { "State", "IsAtRisk", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Actions_Type_RepositoryId_SubjectType_SubjectNumber_AssigneeType_AssigneeId",
                table: "Actions",
                columns: new[] { "Type", "RepositoryId", "SubjectType", "SubjectNumber", "AssigneeType", "AssigneeId" },
                unique: true,
                filter: "[State] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ActionSuppressions_InstallationId_RepositoryId_SubjectType_SubjectNumber_AssigneeType_AssigneeId_IsActive",
                table: "ActionSuppressions",
                columns: new[] { "InstallationId", "RepositoryId", "SubjectType", "SubjectNumber", "AssigneeType", "AssigneeId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionSuppressions_NeedlyUserId_InstallationId_RepositoryId_SubjectType_SubjectNumber_AssigneeType_AssigneeId",
                table: "ActionSuppressions",
                columns: new[] { "NeedlyUserId", "InstallationId", "RepositoryId", "SubjectType", "SubjectNumber", "AssigneeType", "AssigneeId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ActionSuppressions_RepositoryId",
                table: "ActionSuppressions",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_NeedlyUserId_NormalizedName",
                table: "AutomationRules",
                columns: new[] { "NeedlyUserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_NeedlyUserId_SortOrder",
                table: "AutomationRules",
                columns: new[] { "NeedlyUserId", "SortOrder" });

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
                name: "IX_GitHubResponseStates_RepositoryId_SubjectType_SubjectNumber_GitHubAssigneeId",
                table: "GitHubResponseStates",
                columns: new[] { "RepositoryId", "SubjectType", "SubjectNumber", "GitHubAssigneeId" },
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

            migrationBuilder.CreateIndex(
                name: "IX_GitHubUsers_GitHubUserId",
                table: "GitHubUsers",
                column: "GitHubUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubUsers_Login",
                table: "GitHubUsers",
                column: "Login",
                unique: true);

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
                name: "IX_Installations_GitHubInstallationId",
                table: "Installations",
                column: "GitHubInstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NeedlyUsers_Email",
                table: "NeedlyUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NeedlyUsers_GitHubUserId",
                table: "NeedlyUsers",
                column: "GitHubUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_DeliveryId",
                table: "RawEvents",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_GitHubInstallationId_GitHubRepositoryId_ReceivedAt",
                table: "RawEvents",
                columns: new[] { "GitHubInstallationId", "GitHubRepositoryId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_InstallationId",
                table: "RawEvents",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_RepositoryId",
                table: "RawEvents",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_Status_NextAttemptAt",
                table: "RawEvents",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_InstallationId_GitHubRepositoryId",
                table: "Repositories",
                columns: new[] { "InstallationId", "GitHubRepositoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Owner_Name",
                table: "Repositories",
                columns: new[] { "Owner", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleExecutions_ActionId",
                table: "RuleExecutions",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleExecutions_IdempotencyKey",
                table: "RuleExecutions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleExecutions_NeedlyUserId_ExecutedAt",
                table: "RuleExecutions",
                columns: new[] { "NeedlyUserId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_NeedlyUserId_NormalizedName",
                table: "SavedViews",
                columns: new[] { "NeedlyUserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_NeedlyUserId_SortOrder",
                table: "SavedViews",
                columns: new[] { "NeedlyUserId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_GitHubUserId_IsActive",
                table: "TeamMembers",
                columns: new[] { "GitHubUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_GitHubUserId",
                table: "TeamMembers",
                columns: new[] { "TeamId", "GitHubUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_InstallationId_GitHubTeamId",
                table: "Teams",
                columns: new[] { "InstallationId", "GitHubTeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_InstallationId_Slug",
                table: "Teams",
                columns: new[] { "InstallationId", "Slug" },
                unique: true);

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
                name: "ActionDispositions");

            migrationBuilder.DropTable(
                name: "ActionEventReceipts");

            migrationBuilder.DropTable(
                name: "ActionLifecycleUndos");

            migrationBuilder.DropTable(
                name: "AutomationRules");

            migrationBuilder.DropTable(
                name: "GitHubCheckFailureStates");

            migrationBuilder.DropTable(
                name: "GitHubPullRequestStates");

            migrationBuilder.DropTable(
                name: "GitHubResponseStates");

            migrationBuilder.DropTable(
                name: "GitHubReviewerFeedbackStates");

            migrationBuilder.DropTable(
                name: "GitHubReviewRequestStates");

            migrationBuilder.DropTable(
                name: "InstallationMembers");

            migrationBuilder.DropTable(
                name: "RuleExecutions");

            migrationBuilder.DropTable(
                name: "SavedViews");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "UserInstallations");

            migrationBuilder.DropTable(
                name: "RawEvents");

            migrationBuilder.DropTable(
                name: "ActionSuppressions");

            migrationBuilder.DropTable(
                name: "Actions");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "NeedlyUsers");

            migrationBuilder.DropTable(
                name: "Repositories");

            migrationBuilder.DropTable(
                name: "GitHubUsers");

            migrationBuilder.DropTable(
                name: "Installations");
        }
    }
}
