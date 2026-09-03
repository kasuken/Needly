using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BootstrapHistoricalActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HistoricalBootstrapCompletedAt",
                table: "Repositories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HistoricalBootstrapStartedAt",
                table: "Repositories",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HistoricalBootstrapCompletedAt",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "HistoricalBootstrapStartedAt",
                table: "Repositories");
        }
    }
}
