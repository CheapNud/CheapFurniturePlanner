using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class Phase5AuthoringStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthoringMasters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BundleJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthoringMasters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthoringModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModelCode = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    BundleJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthoringModels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthoringModels_ModelCode",
                table: "AuthoringModels",
                column: "ModelCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthoringMasters");

            migrationBuilder.DropTable(
                name: "AuthoringModels");
        }
    }
}
