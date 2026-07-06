using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Catalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileAttachments");

            migrationBuilder.AlterColumn<int>(
                name: "FurnitureItemId",
                table: "PlannerFurnitureItems",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<decimal>(
                name: "CachedUnitPrice",
                table: "PlannerFurnitureItems",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CachedVariantCode",
                table: "PlannerFurnitureItems",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatalogueVersion",
                table: "PlannerFurnitureItems",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElementCode",
                table: "PlannerFurnitureItems",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FabricColorCode",
                table: "PlannerFurnitureItems",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectionsJson",
                table: "PlannerFurnitureItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PublishedCatalogues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BundleJson = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "DATETIME('now')"),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedCatalogues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserNotificationPreference",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    NotificationType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EnabledChannels = table.Column<int>(type: "INTEGER", nullable: false),
                    DoNotDisturbStartHour = table.Column<int>(type: "INTEGER", nullable: true),
                    DoNotDisturbEndHour = table.Column<int>(type: "INTEGER", nullable: true),
                    FurnitureUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationPreference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotificationPreference_AspNetUsers_FurnitureUserId",
                        column: x => x.FurnitureUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublishedCatalogues_IsCurrent",
                table: "PublishedCatalogues",
                column: "IsCurrent");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedCatalogues_Version",
                table: "PublishedCatalogues",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationPreference_FurnitureUserId",
                table: "UserNotificationPreference",
                column: "FurnitureUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublishedCatalogues");

            migrationBuilder.DropTable(
                name: "UserNotificationPreference");

            migrationBuilder.DropColumn(
                name: "CachedUnitPrice",
                table: "PlannerFurnitureItems");

            migrationBuilder.DropColumn(
                name: "CachedVariantCode",
                table: "PlannerFurnitureItems");

            migrationBuilder.DropColumn(
                name: "CatalogueVersion",
                table: "PlannerFurnitureItems");

            migrationBuilder.DropColumn(
                name: "ElementCode",
                table: "PlannerFurnitureItems");

            migrationBuilder.DropColumn(
                name: "FabricColorCode",
                table: "PlannerFurnitureItems");

            migrationBuilder.DropColumn(
                name: "SelectionsJson",
                table: "PlannerFurnitureItems");

            migrationBuilder.AlterColumn<int>(
                name: "FurnitureItemId",
                table: "PlannerFurnitureItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "FileAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "DATETIME('now')"),
                    CreatedById = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Discriminator = table.Column<string>(type: "TEXT", maxLength: 21, nullable: false),
                    DisplayIndex = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    FileExtension = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "DATETIME('now')"),
                    UpdatedById = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Visible = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileAttachments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileAttachments_CreatedAt",
                table: "FileAttachments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FileAttachments_Entity",
                table: "FileAttachments",
                columns: new[] { "EntityId", "EntityType" });

            migrationBuilder.CreateIndex(
                name: "IX_FileAttachments_MimeType",
                table: "FileAttachments",
                column: "MimeType");

            migrationBuilder.CreateIndex(
                name: "IX_FileAttachments_Visible",
                table: "FileAttachments",
                column: "Visible");
        }
    }
}
