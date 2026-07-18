using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class OE3bModelTypeEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ModelTypeCode was a free-text column; ModelType is the closed enum replacing it (stored
            // as INTEGER). Any existing free-text values are dropped here — there is no valid mapping
            // from arbitrary text to the fixed enum set.
            migrationBuilder.DropColumn(
                name: "ModelTypeCode",
                table: "DiscountRules");

            migrationBuilder.AddColumn<int>(
                name: "ModelType",
                table: "DiscountRules",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelType",
                table: "DiscountRules");

            migrationBuilder.AddColumn<string>(
                name: "ModelTypeCode",
                table: "DiscountRules",
                type: "TEXT",
                nullable: true);
        }
    }
}
