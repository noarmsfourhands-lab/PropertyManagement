using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoundUserNamesAndUniqueLeasePerUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UnitTypes_IsActive",
                table: "UnitTypes");

            migrationBuilder.DropIndex(
                name: "IX_Leases_UnitId_StartDate_EndDate",
                table: "Leases");

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Leases_UnitId_StartDate",
                table: "Leases",
                columns: new[] { "UnitId", "StartDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leases_UnitId_StartDate",
                table: "Leases");

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "IX_UnitTypes_IsActive",
                table: "UnitTypes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Leases_UnitId_StartDate_EndDate",
                table: "Leases",
                columns: new[] { "UnitId", "StartDate", "EndDate" });
        }
    }
}
