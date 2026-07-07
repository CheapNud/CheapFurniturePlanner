using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

// Phase 5: the persisted authoring catalogue document store — per-model JSON docs
// keyed uniquely by ModelCode, plus one shared masters doc.
public class Phase5SchemaTests
{
    // The context does not own the Sqlite connection, so callers must dispose the connection
    // alongside the context to avoid leaking it (the connection must stay open for the lifetime
    // of the in-memory database, hence it isn't disposed here).
    private static (FurniturePlannerContext Context, SqliteConnection Connection) NewContext()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        var ctx = new FurniturePlannerContext(options);
        ctx.Database.Migrate();
        return (ctx, conn);
    }

    [Fact]
    public void AuthoringModelDocument_RoundTrips()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        ctx.AuthoringModels.Add(new AuthoringModelDocument { ModelCode = "FJORD-STUDIO", SortOrder = 0, BundleJson = "{}" });
        ctx.SaveChanges();

        var row = ctx.AuthoringModels.Single();
        Assert.Equal("FJORD-STUDIO", row.ModelCode);
        Assert.Equal(0, row.SortOrder);
        Assert.Equal("{}", row.BundleJson);
    }

    [Fact]
    public void AuthoringMastersDocument_RoundTrips()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        ctx.AuthoringMasters.Add(new AuthoringMastersDocument { BundleJson = "{\"a\":1}" });
        ctx.SaveChanges();

        var row = ctx.AuthoringMasters.Single();
        Assert.Equal("{\"a\":1}", row.BundleJson);
    }

    [Fact]
    public void AuthoringModelDocument_DuplicateModelCode_IsRejected()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        ctx.AuthoringModels.Add(new AuthoringModelDocument { ModelCode = "FJORD-STUDIO", SortOrder = 0, BundleJson = "{}" });
        ctx.SaveChanges();

        ctx.AuthoringModels.Add(new AuthoringModelDocument { ModelCode = "FJORD-STUDIO", SortOrder = 1, BundleJson = "{}" });
        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }
}
