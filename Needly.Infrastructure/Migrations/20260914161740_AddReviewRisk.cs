using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReviewRiskLevel",
                table: "Actions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewRiskSignals",
                table: "Actions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewRiskLevel",
                table: "Actions");

            migrationBuilder.DropColumn(
                name: "ReviewRiskSignals",
                table: "Actions");
        }
    }
}
