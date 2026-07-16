using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheapFurniturePlanner.Migrations
{
    /// <inheritdoc />
    public partial class OE1AbsorbVariantNamings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Staged for VariantNamingAbsorber (startup) — converted into the articles document,
            // then dropped there. Rename, not drop, so no naming data is lost on upgrade.
            migrationBuilder.RenameTable(name: "VariantNamings", newName: "LegacyVariantNamings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "LegacyVariantNamings", newName: "VariantNamings");
        }
    }
}
