using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAWS.infrastructure.Migrations.TenantErpDb
{
    /// <inheritdoc />
    public partial class AddBranches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the Branches table FIRST, with no foreign key yet,
            //    so we have somewhere to insert "Main Branch" into.
            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    BranchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.BranchId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_BranchName",
                table: "Branches",
                column: "BranchName",
                unique: true);

            // 2. Insert this tenant's own "Main Branch" row. Its BranchId is
            //    whatever SQL Server's IDENTITY assigns (normally 1, since
            //    this table is brand new).
            migrationBuilder.Sql(
                @"INSERT INTO Branches (BranchName, Address, IsActive, CreatedAt)
                  VALUES ('Main Branch', NULL, 1, GETUTCDATE());");

            // 3. Add BranchId to Pets, temporarily nullable so existing rows
            //    do not need a value yet.
            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Pets",
                type: "int",
                nullable: true);

            // 4. Point every existing pet at the "Main Branch" row we just
            //    created, using its actual generated BranchId rather than
            //    assuming it is 1.
            migrationBuilder.Sql(
                @"UPDATE Pets
                  SET BranchId = (SELECT TOP 1 BranchId FROM Branches WHERE BranchName = 'Main Branch')
                  WHERE BranchId IS NULL;");

            // 5. Now that every row has a value, make the column required.
            migrationBuilder.AlterColumn<int>(
                name: "BranchId",
                table: "Pets",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pets_BranchId",
                table: "Pets",
                column: "BranchId");

            // 6. Only now add the foreign key, once every row is valid.
            migrationBuilder.AddForeignKey(
                name: "FK_Pets_Branches_BranchId",
                table: "Pets",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pets_Branches_BranchId",
                table: "Pets");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Pets_BranchId",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Pets");
        }
    }
}