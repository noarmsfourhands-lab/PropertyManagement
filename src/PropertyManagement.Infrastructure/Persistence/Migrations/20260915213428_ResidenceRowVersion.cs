using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives each residence row its own concurrency token.
    ///
    /// The section's token guards the section's save and deliberately does not move when a row
    /// changes, because the modal is opened from the page holding it. That left one gap: two
    /// applicants editing the same residence, where the second save silently wrote over the first.
    /// A token on the row closes it without touching the page.
    /// </summary>
    public partial class ResidenceRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Residences",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Existing rows would otherwise all share the zero guid. Nothing breaks either way,
            // because the check compares a row against itself, but a column of zeroes reads as a
            // column nobody filled in.
            migrationBuilder.Sql("UPDATE Residences SET Version = NEWID();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Residences");
        }
    }
}
