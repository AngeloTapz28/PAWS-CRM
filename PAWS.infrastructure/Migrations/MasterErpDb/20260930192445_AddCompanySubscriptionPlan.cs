using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAWS.infrastructure.Migrations.MasterErpDb
{
    /// <inheritdoc />
    public partial class AddCompanySubscriptionPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
            name: "SubscriptionPlan",
            table: "Companies",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Free");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscriptionPlan",
                table: "Companies");
        }
    }
}
