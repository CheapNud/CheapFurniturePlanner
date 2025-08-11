using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", nullable: false),
                    LastName = table.Column<string>(type: "TEXT", nullable: false),
                    IsDarkMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    NavigationStateJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "{}"),
                    PreferredLanguage = table.Column<string>(type: "TEXT", nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsFirstLogin = table.Column<bool>(type: "INTEGER", nullable: false),
                    TimeZoneInfoId = table.Column<string>(type: "TEXT", nullable: true),
                    PinCodeHash = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Visible = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    DisplayIndex = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "DATETIME('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "DATETIME('now')"),
                    CreatedById = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    UpdatedById = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FileExtension = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Discriminator = table.Column<string>(type: "TEXT", maxLength: 21, nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FurnitureItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Length = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    Weight = table.Column<double>(type: "REAL", nullable: true),
                    Color = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Material = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Price = table.Column<decimal>(type: "REAL", nullable: true),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "DATETIME('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FurnitureItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "cm"),
                    GridSize = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 10),
                    ShowGrid = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    PreventOverlap = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    EnableSnapping = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "DATETIME('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlannerFurnitureItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomPlanId = table.Column<int>(type: "INTEGER", nullable: false),
                    FurnitureItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    UIId = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Rotation = table.Column<double>(type: "REAL", nullable: false, defaultValue: 0.0),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "DATETIME('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerFurnitureItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannerFurnitureItems_FurnitureItems_FurnitureItemId",
                        column: x => x.FurnitureItemId,
                        principalTable: "FurnitureItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlannerFurnitureItems_RoomPlans_RoomPlanId",
                        column: x => x.RoomPlanId,
                        principalTable: "RoomPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "FurnitureItems",
                columns: new[] { "Id", "Brand", "Code", "Color", "CreatedAt", "Description", "Height", "ImageUrl", "IsActive", "Length", "Material", "Model", "Name", "Price", "Type", "UpdatedAt", "Weight", "Width" },
                values: new object[,]
                {
                    { 1, "CheapFurniture", "CHEAP-SOFA-001", "Gray", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Comfortable 3-seat sofa for living room", 85.0, null, true, 90.0, "Fabric", "Comfort Plus", "Cheap 3-Seat Sofa", 599.99m, 1, null, 45.0, 200.0 },
                    { 2, "CheapOffice", "CHEAP-CHAIR-001", "Black", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Ergonomic office chair with adjustable height", 120.0, null, true, 60.0, "Mesh/Plastic", "Ergo Basic", "Cheap Office Chair", 199.99m, 2, null, 15.0, 60.0 },
                    { 3, "CheapWood", "CHEAP-TABLE-001", "Oak", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Rectangular dining table for 6 people", 75.0, null, true, 90.0, "Wood", "Family", "Cheap Dining Table", 399.99m, 13, null, 35.0, 160.0 },
                    { 4, "CheapSleep", "CHEAP-BED-001", "White", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Queen size bed frame with headboard", 100.0, null, true, 200.0, "Wood/Metal", "Dream Queen", "Cheap Queen Bed", 299.99m, 4, null, 40.0, 160.0 },
                    { 5, "CheapStyle", "CHEAP-COFFEE-001", "Walnut", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Modern coffee table with storage", 45.0, null, true, 60.0, "Wood", "Modern Store", "Cheap Coffee Table", 149.99m, 14, null, 20.0, 120.0 }
                });

            migrationBuilder.InsertData(
                table: "RoomPlans",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "Description", "EnableSnapping", "GridSize", "Height", "Name", "PreventOverlap", "ShowGrid", "Unit", "UpdatedAt", "Width" },
                values: new object[] { 1, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "System", "A sample living room layout", true, 10, 400.0, "Sample Living Room", true, true, "cm", null, 500.0 });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_FurnitureItems_Code",
                table: "FurnitureItems",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FurnitureItems_IsActive",
                table: "FurnitureItems",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_FurnitureItems_Name",
                table: "FurnitureItems",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_FurnitureItems_Type",
                table: "FurnitureItems",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_PlannerFurnitureItems_FurnitureItemId",
                table: "PlannerFurnitureItems",
                column: "FurnitureItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannerFurnitureItems_GroupId",
                table: "PlannerFurnitureItems",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannerFurnitureItems_RoomPlanId",
                table: "PlannerFurnitureItems",
                column: "RoomPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannerFurnitureItems_RoomPlanId_UIId",
                table: "PlannerFurnitureItems",
                columns: new[] { "RoomPlanId", "UIId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomPlans_CreatedAt",
                table: "RoomPlans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RoomPlans_CreatedBy",
                table: "RoomPlans",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RoomPlans_Name",
                table: "RoomPlans",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "FileAttachments");

            migrationBuilder.DropTable(
                name: "PlannerFurnitureItems");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "FurnitureItems");

            migrationBuilder.DropTable(
                name: "RoomPlans");
        }
    }
}
