using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActionSubjectFilterMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "Actions",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Labels",
                table: "Actions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Milestone",
                table: "Actions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequestedViaCodeowners",
                table: "Actions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SizeBucket",
                table: "Actions",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "Actions");

            migrationBuilder.DropColumn(
                name: "Labels",
                table: "Actions");

            migrationBuilder.DropColumn(
                name: "Milestone",
                table: "Actions");

            migrationBuilder.DropColumn(
                name: "RequestedViaCodeowners",
                table: "Actions");

            migrationBuilder.DropColumn(
                name: "SizeBucket",
                table: "Actions");
        }
    }
}
