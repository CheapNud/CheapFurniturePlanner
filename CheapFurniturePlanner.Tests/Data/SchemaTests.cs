using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

public class SchemaTests
{
    private static FurniturePlannerContext NewContext()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        var ctx = new FurniturePlannerContext(options);
        ctx.Database.Migrate();
        return ctx;
    }

    [Fact]
    public void PublishedCatalogue_RoundTrips()
    {
        using var ctx = NewContext();
        ctx.PublishedCatalogues.Add(new PublishedCatalogue { Version = "1", ContentHash = "abc", BundleJson = "{}", IsCurrent = true });
        ctx.SaveChanges();
        var row = ctx.PublishedCatalogues.Single();
        Assert.True(row.IsCurrent);
        Assert.Equal("1", row.Version);
    }

    [Fact]
    public void Placement_StoresConfiguration_WithoutFlatItem()
    {
        using var ctx = NewContext();
        var room = new RoomPlan { Name = "R", Width = 100, Height = 100 };
        ctx.RoomPlans.Add(room); ctx.SaveChanges();
        ctx.PlannerFurnitureItems.Add(new PlannerFurnitureItem
        {
            RoomPlanId = room.Id, UIId = 1, X = 0, Y = 0,
            ElementCode = "FJ2", CatalogueVersion = "1",
            SelectionsJson = "{\"DEPTH\":\"STD\"}", FabricColorCode = "AQUA-BLUE",
            CachedVariantCode = "FJ2-__MATERIAL__:Fabric", CachedUnitPrice = 407m
        });
        ctx.SaveChanges();
        var p = ctx.PlannerFurnitureItems.Single();
        Assert.Null(p.FurnitureItemId);
        Assert.Equal("FJ2", p.ElementCode);
    }
}
