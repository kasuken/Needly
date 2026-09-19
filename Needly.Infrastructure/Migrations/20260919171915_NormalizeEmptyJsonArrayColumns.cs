using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeEmptyJsonArrayColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Labels and ReviewRiskSignals columns store string[] as JSON. They were
            // introduced as non-nullable with an empty-string default, so rows that pre-existed
            // each column were backfilled with "" — a value JsonSerializer.Deserialize rejects,
            // crashing every Inbox read. Backfill those rows with a valid empty JSON array.
            migrationBuilder.Sql(
                "UPDATE [Actions] SET [ReviewRiskSignals] = '[]' WHERE [ReviewRiskSignals] = '' OR [ReviewRiskSignals] IS NULL;");
            migrationBuilder.Sql(
                "UPDATE [Actions] SET [Labels] = '[]' WHERE [Labels] = '' OR [Labels] IS NULL;");

            // Reset the column defaults so any future default-backfilled rows are valid JSON.
            migrationBuilder.AlterColumn<string>(
                name: "ReviewRiskSignals",
                table: "Actions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: false,
                oldDefaultValue: "");
            migrationBuilder.AlterColumn<string>(
                name: "Labels",
                table: "Actions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: false,
                oldDefaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the column defaults are reverted; the data backfill is intentionally not undone
            // because empty strings would reintroduce the deserialization failure.
            migrationBuilder.AlterColumn<string>(
                name: "ReviewRiskSignals",
                table: "Actions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: false,
                oldDefaultValue: "[]");
            migrationBuilder.AlterColumn<string>(
                name: "Labels",
                table: "Actions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: false,
                oldDefaultValue: "[]");
        }
    }
}
