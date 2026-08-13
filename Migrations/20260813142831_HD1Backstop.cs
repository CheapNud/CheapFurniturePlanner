using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class HD1Backstop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConsumerDeliveryAddresses_ConsumerId",
                table: "ConsumerDeliveryAddresses");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerDeliveryAddresses_ConsumerId_OneDefault",
                table: "ConsumerDeliveryAddresses",
                column: "ConsumerId",
                unique: true,
                filter: "IsDefault = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConsumerDeliveryAddresses_ConsumerId_OneDefault",
                table: "ConsumerDeliveryAddresses");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerDeliveryAddresses_ConsumerId",
                table: "ConsumerDeliveryAddresses",
                column: "ConsumerId");
        }
    }
}
