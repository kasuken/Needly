using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SavedViewsAndAutomationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorLogin",
                table: "Actions",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasBotInvolvement",
                table: "Actions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ActionDispositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMuted = table.Column<bool>(type: "INTEGER", nullable: false),
                    SnoozedUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsFyi = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
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
                name: "AutomationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FilterJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    Effect = table.Column<int>(type: "INTEGER", nullable: false),
                    SnoozeDuration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
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
                name: "RuleExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Effect = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 140, nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
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
                name: "SavedViews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FilterJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
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
                name: "IX_AutomationRules_NeedlyUserId_NormalizedName",
                table: "AutomationRules",
                columns: new[] { "NeedlyUserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_NeedlyUserId_SortOrder",
                table: "AutomationRules",
                columns: new[] { "NeedlyUserId", "SortOrder" });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionDispositions");

            migrationBuilder.DropTable(
                name: "AutomationRules");

            migrationBuilder.DropTable(
                name: "RuleExecutions");

            migrationBuilder.DropTable(
                name: "SavedViews");

            migrationBuilder.DropColumn(
                name: "AuthorLogin",
                table: "Actions");

            migrationBuilder.DropColumn(
                name: "HasBotInvolvement",
                table: "Actions");
        }
    }
}
