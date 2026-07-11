using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class PriceP1EffectiveDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveDate",
                table: "PublishedCatalogues",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql("UPDATE \"PublishedCatalogues\" SET \"EffectiveDate\" = \"PublishedAt\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                table: "PublishedCatalogues");
        }
    }
}
