using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActionLifecycleInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SnoozedUntil",
                table: "Actions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActionSuppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AssigneeType = table.Column<int>(type: "INTEGER", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionSuppressions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionSuppressions_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "ActionLifecycleUndos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NeedlyUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviousState = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousSnoozedUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AppliedState = table.Column<int>(type: "INTEGER", nullable: false),
                    SuppressionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionLifecycleUndos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionLifecycleUndos_ActionSuppressions_SuppressionId",
                        column: x => x.SuppressionId,
                        principalTable: "ActionSuppressions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "IX_ActionSuppressions_InstallationId_RepositoryId_SubjectType_SubjectNumber_AssigneeType_AssigneeId_IsActive",
                table: "ActionSuppressions",
                columns: new[] { "InstallationId", "RepositoryId", "SubjectType", "SubjectNumber", "AssigneeType", "AssigneeId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionSuppressions_NeedlyUserId_InstallationId_RepositoryId_SubjectType_SubjectNumber_AssigneeType_AssigneeId",
                table: "ActionSuppressions",
                columns: new[] { "NeedlyUserId", "InstallationId", "RepositoryId", "SubjectType", "SubjectNumber", "AssigneeType", "AssigneeId" },
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ActionSuppressions_RepositoryId",
                table: "ActionSuppressions",
                column: "RepositoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionLifecycleUndos");

            migrationBuilder.DropTable(
                name: "ActionSuppressions");

            migrationBuilder.DropColumn(
                name: "SnoozedUntil",
                table: "Actions");
        }
    }
}
