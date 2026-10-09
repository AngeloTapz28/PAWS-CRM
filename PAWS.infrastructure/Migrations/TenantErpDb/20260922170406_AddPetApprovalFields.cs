using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAWS.infrastructure.Migrations.TenantErpDb
{
    /// <inheritdoc />
    public partial class AddPetApprovalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdminApprovedBy",
                table: "Pets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VetNotes",
                table: "Pets",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pets_AdminApprovedBy",
                table: "Pets",
                column: "AdminApprovedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Pets_Users_AdminApprovedBy",
                table: "Pets",
                column: "AdminApprovedBy",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pets_Users_AdminApprovedBy",
                table: "Pets");

            migrationBuilder.DropIndex(
                name: "IX_Pets_AdminApprovedBy",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "AdminApprovedBy",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "VetNotes",
                table: "Pets");
        }
    }
}
