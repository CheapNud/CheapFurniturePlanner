using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class AD1Addresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "SupplierReports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AddressId",
                table: "Sellers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryAddressId",
                table: "Orders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "OrderLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryAddressId",
                table: "Consumers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Addresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Street = table.Column<string>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", nullable: false),
                    Box = table.Column<string>(type: "TEXT", nullable: true),
                    PostalCode = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: false),
                    RegionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Addresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Addresses_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ConsumerDeliveryAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsumerId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddressId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumerDeliveryAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsumerDeliveryAddresses_Addresses_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Addresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsumerDeliveryAddresses_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AddressId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Suppliers_Addresses_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Addresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReports_SupplierId",
                table: "SupplierReports",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Sellers_AddressId",
                table: "Sellers",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DeliveryAddressId",
                table: "Orders",
                column: "DeliveryAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_SupplierId",
                table: "OrderLines",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Consumers_PrimaryAddressId",
                table: "Consumers",
                column: "PrimaryAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_RegionId",
                table: "Addresses",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerDeliveryAddresses_AddressId",
                table: "ConsumerDeliveryAddresses",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerDeliveryAddresses_ConsumerId",
                table: "ConsumerDeliveryAddresses",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Code",
                table: "Regions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_AddressId",
                table: "Suppliers",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Code",
                table: "Suppliers",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Consumers_Addresses_PrimaryAddressId",
                table: "Consumers",
                column: "PrimaryAddressId",
                principalTable: "Addresses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_Suppliers_SupplierId",
                table: "OrderLines",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Addresses_DeliveryAddressId",
                table: "Orders",
                column: "DeliveryAddressId",
                principalTable: "Addresses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sellers_Addresses_AddressId",
                table: "Sellers",
                column: "AddressId",
                principalTable: "Addresses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierReports_Suppliers_SupplierId",
                table: "SupplierReports",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Consumers_Addresses_PrimaryAddressId",
                table: "Consumers");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_Suppliers_SupplierId",
                table: "OrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Addresses_DeliveryAddressId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Sellers_Addresses_AddressId",
                table: "Sellers");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierReports_Suppliers_SupplierId",
                table: "SupplierReports");

            migrationBuilder.DropTable(
                name: "ConsumerDeliveryAddresses");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "Addresses");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_SupplierReports_SupplierId",
                table: "SupplierReports");

            migrationBuilder.DropIndex(
                name: "IX_Sellers_AddressId",
                table: "Sellers");

            migrationBuilder.DropIndex(
                name: "IX_Orders_DeliveryAddressId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_SupplierId",
                table: "OrderLines");

            migrationBuilder.DropIndex(
                name: "IX_Consumers_PrimaryAddressId",
                table: "Consumers");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "SupplierReports");

            migrationBuilder.DropColumn(
                name: "AddressId",
                table: "Sellers");

            migrationBuilder.DropColumn(
                name: "DeliveryAddressId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "PrimaryAddressId",
                table: "Consumers");
        }
    }
}
