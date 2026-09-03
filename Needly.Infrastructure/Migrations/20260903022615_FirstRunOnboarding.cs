using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Needly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FirstRunOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingCompletedAt",
                table: "NeedlyUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAt",
                table: "NeedlyUsers");
        }
    }
}
