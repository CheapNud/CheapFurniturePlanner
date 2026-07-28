using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class DP1Planning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Trips",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RegionId",
                table: "Trips",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PromisedDeliveryDate",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_RegionId",
                table: "Trips",
                column: "RegionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_Regions_RegionId",
                table: "Trips",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Regions_RegionId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_RegionId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "PromisedDeliveryDate",
                table: "Orders");
        }
    }
}
