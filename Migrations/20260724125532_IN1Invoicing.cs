using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class IN1Invoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VatNumber",
                table: "Consumers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceNumber = table.Column<string>(type: "TEXT", nullable: false),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OrderDiscountPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    NetTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    VatTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    GrossTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketVatRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MarketCode = table.Column<string>(type: "TEXT", nullable: false),
                    RatePercent = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketVatRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreditNoteNumber = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    NetAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    VatAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSettled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    VatAmount = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CreditNoteNumber",
                table: "CreditNotes",
                column: "CreditNoteNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrderId",
                table: "Invoices",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketVatRates_MarketCode",
                table: "MarketVatRates",
                column: "MarketCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditNotes");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "MarketVatRates");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropColumn(
                name: "VatNumber",
                table: "Consumers");
        }
    }
}
