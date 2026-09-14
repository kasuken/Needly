using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentAuthor",
                table: "Actions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentDisplayName",
                table: "Actions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentAuthor",
                table: "Actions");

            migrationBuilder.DropColumn(
                name: "AgentDisplayName",
                table: "Actions");
        }
    }
}
