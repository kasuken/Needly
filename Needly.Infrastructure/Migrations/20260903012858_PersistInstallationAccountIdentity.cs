using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistInstallationAccountIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "GitHubAccountId",
                table: "Installations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Installations"
                SET "GitHubAccountId" = (
                    SELECT "GitHubUserId"
                    FROM "GitHubUsers"
                    WHERE lower("GitHubUsers"."Login") = lower("Installations"."AccountLogin")
                )
                WHERE "AccountType" = 0
                  AND "GitHubAccountId" IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "GitHubUsers"
                      WHERE lower("GitHubUsers"."Login") = lower("Installations"."AccountLogin")
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitHubAccountId",
                table: "Installations");
        }
    }
}
