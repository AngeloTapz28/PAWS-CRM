using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAWS.infrastructure.Migrations.MasterErpDb
{
    /// <inheritdoc />
    public partial class AddTermsAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TermsUpdatedAt",
                table: "SubscriptionPlans",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedAt",
                table: "Companies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAcceptedByName",
                table: "Companies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TermsAcceptedPlanId",
                table: "Companies",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "SubscriptionPlanId",
                keyValue: 1,
                column: "TermsUpdatedAt",
                value: new DateTime(2026, 10, 5, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "SubscriptionPlanId",
                keyValue: 2,
                column: "TermsUpdatedAt",
                value: new DateTime(2026, 10, 5, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "SubscriptionPlanId",
                keyValue: 3,
                column: "TermsUpdatedAt",
                value: new DateTime(2026, 10, 5, 0, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TermsUpdatedAt",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedByName",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedPlanId",
                table: "Companies");
        }
    }
}
