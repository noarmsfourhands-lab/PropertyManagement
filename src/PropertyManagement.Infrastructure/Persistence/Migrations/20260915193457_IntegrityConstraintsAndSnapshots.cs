using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Turns four rules that lived only in code into rules the database keeps.
    ///
    /// Real foreign keys on the two columns read as authority, so an account cannot be deleted out
    /// from under an application that it alone can open. Restrict instead of cascade on the audit
    /// trail and on manager notes, so deleting an application can never quietly take its history
    /// with it. A check constraint saying a submitted or decided application carries its applicant's
    /// details. And the property and unit names copied onto applications and leases, so a record
    /// keeps describing the same thing after the unit it points at has been renumbered.
    ///
    /// The two backfills matter: without them every application and lease that already exists would
    /// describe itself as an empty string.
    /// </summary>
    public partial class IntegrityConstraintsAndSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationEvents_RentalApplications_RentalApplicationId",
                table: "ApplicationEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_PropertyManagerNotes_RentalApplications_RentalApplicationId",
                table: "PropertyManagerNotes");

            migrationBuilder.AddColumn<string>(
                name: "PropertyName",
                table: "RentalApplications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UnitNumber",
                table: "RentalApplications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PropertyName",
                table: "Leases",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UnitNumber",
                table: "Leases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Existing rows describe themselves from the unit they point at, once, now. From here
            // on the application and the lease record these for themselves as they are created.
            migrationBuilder.Sql(
                """
                UPDATE application
                SET application.PropertyName = property.Name,
                    application.UnitNumber = unit.UnitNumber
                FROM RentalApplications AS application
                INNER JOIN Units AS unit ON unit.Id = application.UnitId
                INNER JOIN Properties AS property ON property.Id = unit.PropertyId;
                """);

            migrationBuilder.Sql(
                """
                UPDATE lease
                SET lease.PropertyName = property.Name,
                    lease.UnitNumber = unit.UnitNumber
                FROM Leases AS lease
                INNER JOIN Units AS unit ON unit.Id = lease.UnitId
                INNER JOIN Properties AS property ON property.Id = unit.PropertyId;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RentalApplications_ClaimedByUserId",
                table: "RentalApplications",
                column: "ClaimedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RentalApplications_SubmittedHasApplicantInformation",
                table: "RentalApplications",
                sql: "[Status] NOT IN (1, 2, 4, 5)\nOR ([ApplicantFirstName] IS NOT NULL\n    AND [ApplicantLastName] IS NOT NULL\n    AND [ApplicantPhone] IS NOT NULL\n    AND [ApplicantEmail] IS NOT NULL\n    AND [ApplicantAddressLine1] IS NOT NULL\n    AND [ApplicantCity] IS NOT NULL\n    AND [ApplicantState] IS NOT NULL\n    AND [ApplicantPostalCode] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationEvents_RentalApplications_RentalApplicationId",
                table: "ApplicationEvents",
                column: "RentalApplicationId",
                principalTable: "RentalApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PropertyManagerNotes_RentalApplications_RentalApplicationId",
                table: "PropertyManagerNotes",
                column: "RentalApplicationId",
                principalTable: "RentalApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RentalApplicationApplicants_AspNetUsers_ApplicantUserId",
                table: "RentalApplicationApplicants",
                column: "ApplicantUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RentalApplications_AspNetUsers_ClaimedByUserId",
                table: "RentalApplications",
                column: "ClaimedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationEvents_RentalApplications_RentalApplicationId",
                table: "ApplicationEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_PropertyManagerNotes_RentalApplications_RentalApplicationId",
                table: "PropertyManagerNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_RentalApplicationApplicants_AspNetUsers_ApplicantUserId",
                table: "RentalApplicationApplicants");

            migrationBuilder.DropForeignKey(
                name: "FK_RentalApplications_AspNetUsers_ClaimedByUserId",
                table: "RentalApplications");

            migrationBuilder.DropIndex(
                name: "IX_RentalApplications_ClaimedByUserId",
                table: "RentalApplications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RentalApplications_SubmittedHasApplicantInformation",
                table: "RentalApplications");

            migrationBuilder.DropColumn(
                name: "PropertyName",
                table: "RentalApplications");

            migrationBuilder.DropColumn(
                name: "UnitNumber",
                table: "RentalApplications");

            migrationBuilder.DropColumn(
                name: "PropertyName",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "UnitNumber",
                table: "Leases");

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationEvents_RentalApplications_RentalApplicationId",
                table: "ApplicationEvents",
                column: "RentalApplicationId",
                principalTable: "RentalApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PropertyManagerNotes_RentalApplications_RentalApplicationId",
                table: "PropertyManagerNotes",
                column: "RentalApplicationId",
                principalTable: "RentalApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
