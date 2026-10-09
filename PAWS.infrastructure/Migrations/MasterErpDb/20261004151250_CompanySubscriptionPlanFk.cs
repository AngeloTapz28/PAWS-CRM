using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAWS.infrastructure.Migrations.MasterErpDb
{
    /// <inheritdoc />
    public partial class CompanySubscriptionPlanFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add the new columns. The FK column starts NULLABLE so existing
            //    rows are legal while we fill it in. The start date defaults to
            //    "now", so every existing company begins a fresh 30-day cycle.
            migrationBuilder.AddColumn<int>(
                name: "SubscriptionPlanId",
                table: "Companies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionStartDate",
                table: "Companies",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            // 2. Convert each company's old plan NAME ("Basic", "Premium", ...)
            //    into the matching plan ID, BEFORE the old column is dropped.
            migrationBuilder.Sql(@"
                UPDATE c
                SET c.SubscriptionPlanId = p.SubscriptionPlanId
                FROM Companies c
                INNER JOIN SubscriptionPlans p ON p.Name = c.SubscriptionPlan;");

            // 3. Safety net: anything that did not match a plan name falls back to Free (ID 1).
            migrationBuilder.Sql(@"
                UPDATE Companies
                SET SubscriptionPlanId = 1
                WHERE SubscriptionPlanId IS NULL;");

            // 4. Now every row has a valid ID, so the column can be required.
            migrationBuilder.AlterColumn<int>(
                name: "SubscriptionPlanId",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // 5. The old string column is no longer needed.
            migrationBuilder.DropColumn(
                name: "SubscriptionPlan",
                table: "Companies");

            // 6. Index and foreign key.
            migrationBuilder.CreateIndex(
                name: "IX_Companies_SubscriptionPlanId",
                table: "Companies",
                column: "SubscriptionPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_SubscriptionPlans_SubscriptionPlanId",
                table: "Companies",
                column: "SubscriptionPlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "SubscriptionPlanId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_SubscriptionPlans_SubscriptionPlanId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_SubscriptionPlanId",
                table: "Companies");

            // Bring the old string column back and refill it from the plan names.
            migrationBuilder.AddColumn<string>(
                name: "SubscriptionPlan",
                table: "Companies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Free");

            migrationBuilder.Sql(@"
                UPDATE c
                SET c.SubscriptionPlan = LEFT(p.Name, 20)
                FROM Companies c
                INNER JOIN SubscriptionPlans p ON p.SubscriptionPlanId = c.SubscriptionPlanId;");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SubscriptionStartDate",
                table: "Companies");
        }
    }
}