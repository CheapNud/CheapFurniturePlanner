using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

public class SchemaTests
{
    // The context does not own the Sqlite connection, so callers must dispose the connection
    // alongside the context to avoid leaking it (the connection must stay open for the lifetime
    // of the in-memory database, hence it isn't disposed here).
    private static (FurniturePlannerContext Context, Microsoft.Data.Sqlite.SqliteConnection Connection) NewContext()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        var ctx = new FurniturePlannerContext(options);
        ctx.Database.Migrate();
        return (ctx, conn);
    }

    [Fact]
    public void PublishedCatalogue_RoundTrips()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        ctx.PublishedCatalogues.Add(new PublishedCatalogue { Version = "1", ContentHash = "abc", BundleJson = "{}", IsCurrent = true });
        ctx.SaveChanges();
        var row = ctx.PublishedCatalogues.Single();
        Assert.True(row.IsCurrent);
        Assert.Equal("1", row.Version);
    }

    [Fact]
    public void Placement_StoresConfiguration_WithoutFlatItem()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
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
