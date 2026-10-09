using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PAWS.infrastructure.Migrations.MasterErpDb
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    SubscriptionPlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BranchLimit = table.Column<int>(type: "int", nullable: false),
                    TermsAndConditions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.SubscriptionPlanId);
                });

            migrationBuilder.InsertData(
                table: "SubscriptionPlans",
                columns: new[] { "SubscriptionPlanId", "BranchLimit", "CreatedAt", "IsActive", "Name", "Price", "TermsAndConditions" },
                values: new object[,]
                {
                    { 1, 1, new DateTime(2026, 10, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Free", 0m, "FREE PLAN - PLACEHOLDER TERMS. This plan is provided at no cost and allows 1 branch. Features and limits may change. The Super Admin can edit these terms." },
                    { 2, 3, new DateTime(2026, 10, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Basic", 499m, "BASIC PLAN - PLACEHOLDER TERMS. PHP 499 per 30-day cycle. Allows up to 3 branches. The Super Admin can edit these terms." },
                    { 3, 2147483647, new DateTime(2026, 10, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Premium", 1499m, "PREMIUM PLAN - PLACEHOLDER TERMS. PHP 1,499 per 30-day cycle. Unlimited branches. The Super Admin can edit these terms." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_Name",
                table: "SubscriptionPlans",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionPlans");
        }
    }
}
