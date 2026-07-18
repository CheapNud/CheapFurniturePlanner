using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class OE3Discounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OrderDiscountPercent",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "DiscountIsManual",
                table: "OrderLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "OrderLines",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountSource",
                table: "OrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiscountRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SellerId = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    ElementCode = table.Column<string>(type: "TEXT", nullable: true),
                    PriceGroupCode = table.Column<string>(type: "TEXT", nullable: true),
                    ModelCode = table.Column<string>(type: "TEXT", nullable: true),
                    ModelTypeCode = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialTypeCode = table.Column<string>(type: "TEXT", nullable: true),
                    RatePercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    FixedPrice = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountRules", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "OrderDiscountPercent",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DiscountIsManual",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "DiscountSource",
                table: "OrderLines");
        }
    }
}
