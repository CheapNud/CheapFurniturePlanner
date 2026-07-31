using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class SP1Purchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupplierRef",
                table: "SupplierReports");

            migrationBuilder.DropColumn(
                name: "SupplierRef",
                table: "OrderLines");

            migrationBuilder.AddColumn<int>(
                name: "SupplierDeliveryId",
                table: "ProductionUnits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierOrderId",
                table: "ProductionUnits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplierDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierDeliveries_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierModelMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelCode = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierModelMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierModelMaps_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PoNumber = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TheirReference = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierOrders_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionUnits_SupplierDeliveryId",
                table: "ProductionUnits",
                column: "SupplierDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionUnits_SupplierOrderId",
                table: "ProductionUnits",
                column: "SupplierOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDeliveries_SupplierId_Reference",
                table: "SupplierDeliveries",
                columns: new[] { "SupplierId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierModelMaps_ModelCode",
                table: "SupplierModelMaps",
                column: "ModelCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierModelMaps_SupplierId",
                table: "SupplierModelMaps",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierOrders_PoNumber",
                table: "SupplierOrders",
                column: "PoNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierOrders_SupplierId",
                table: "SupplierOrders",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionUnits_SupplierDeliveries_SupplierDeliveryId",
                table: "ProductionUnits",
                column: "SupplierDeliveryId",
                principalTable: "SupplierDeliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionUnits_SupplierOrders_SupplierOrderId",
                table: "ProductionUnits",
                column: "SupplierOrderId",
                principalTable: "SupplierOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductionUnits_SupplierDeliveries_SupplierDeliveryId",
                table: "ProductionUnits");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionUnits_SupplierOrders_SupplierOrderId",
                table: "ProductionUnits");

            migrationBuilder.DropTable(
                name: "SupplierDeliveries");

            migrationBuilder.DropTable(
                name: "SupplierModelMaps");

            migrationBuilder.DropTable(
                name: "SupplierOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionUnits_SupplierDeliveryId",
                table: "ProductionUnits");

            migrationBuilder.DropIndex(
                name: "IX_ProductionUnits_SupplierOrderId",
                table: "ProductionUnits");

            migrationBuilder.DropColumn(
                name: "SupplierDeliveryId",
                table: "ProductionUnits");

            migrationBuilder.DropColumn(
                name: "SupplierOrderId",
                table: "ProductionUnits");

            migrationBuilder.AddColumn<string>(
                name: "SupplierRef",
                table: "SupplierReports",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SupplierRef",
                table: "OrderLines",
                type: "TEXT",
                nullable: true);
        }
    }
}
