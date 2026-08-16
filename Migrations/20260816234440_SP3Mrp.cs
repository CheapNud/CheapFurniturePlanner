using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class SP3Mrp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "MaterialOrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaterialMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    HardnessCode = table.Column<string>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialMovements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    HardnessCode = table.Column<string>(type: "TEXT", nullable: true),
                    MinimumStock = table.Column<decimal>(type: "TEXT", nullable: false),
                    AverageUsageOverride = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialSupplierTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    HardnessCode = table.Column<string>(type: "TEXT", nullable: true),
                    SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeliveryTimeDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumOrderQuantity = table.Column<decimal>(type: "TEXT", nullable: true),
                    UnitsPerPackage = table.Column<decimal>(type: "TEXT", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    IsPreferred = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialSupplierTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialSupplierTerms_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialProfiles_Kind_Code_HardnessCode",
                table: "MaterialProfiles",
                columns: new[] { "Kind", "Code", "HardnessCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSupplierTerms_Kind_Code_HardnessCode_SupplierId",
                table: "MaterialSupplierTerms",
                columns: new[] { "Kind", "Code", "HardnessCode", "SupplierId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSupplierTerms_SupplierId",
                table: "MaterialSupplierTerms",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterialMovements");

            migrationBuilder.DropTable(
                name: "MaterialProfiles");

            migrationBuilder.DropTable(
                name: "MaterialSupplierTerms");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "MaterialOrderLines");
        }
    }
}
