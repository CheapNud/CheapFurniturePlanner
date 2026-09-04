using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class MB1Concurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "ProductionUnits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "AuthoringModels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "AuthoringMasters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "AuthoringArticles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Firms_OneDefault",
                table: "Firms",
                column: "IsDefault",
                unique: true,
                filter: "IsDefault = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRules_SellerId_CollectionCode_Scope_ElementCode_PriceGroupCode_ModelCode_ModelType_MaterialTypeCode",
                table: "DiscountRules",
                columns: new[] { "SellerId", "CollectionCode", "Scope", "ElementCode", "PriceGroupCode", "ModelCode", "ModelType", "MaterialTypeCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Firms_OneDefault",
                table: "Firms");

            migrationBuilder.DropIndex(
                name: "IX_DiscountRules_SellerId_CollectionCode_Scope_ElementCode_PriceGroupCode_ModelCode_ModelType_MaterialTypeCode",
                table: "DiscountRules");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ProductionUnits");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "AuthoringModels");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "AuthoringMasters");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "AuthoringArticles");
        }
    }
}
