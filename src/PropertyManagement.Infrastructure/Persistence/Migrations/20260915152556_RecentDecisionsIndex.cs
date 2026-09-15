using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecentDecisionsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_Decisions",
                table: "ApplicationEvents",
                column: "OccurredAtUtc",
                filter: "[Outcome] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationEvents_Decisions",
                table: "ApplicationEvents");
        }
    }
}
