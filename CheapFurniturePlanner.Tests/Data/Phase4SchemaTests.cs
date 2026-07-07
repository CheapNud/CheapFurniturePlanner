using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

// Phase 4: the sparse VariantNaming registry only ever holds rows for variants the studio
// has actually named, keyed uniquely per (ModelCode, VariantCode).
public class Phase4SchemaTests
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
    public void VariantNaming_RoundTrips()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var now = DateTime.UtcNow;
        ctx.VariantNamings.Add(new VariantNaming
        {
            ModelCode = "FJORD-STUDIO",
            VariantCode = "FJ2-__MATERIAL__:Fabric",
            AssignedCode = "STUDIO-A",
            CreatedAt = now,
            UpdatedAt = now
        });
        ctx.SaveChanges();

        var row = ctx.VariantNamings.Single();
        Assert.Equal("FJORD-STUDIO", row.ModelCode);
        Assert.Equal("FJ2-__MATERIAL__:Fabric", row.VariantCode);
        Assert.Equal("STUDIO-A", row.AssignedCode);
    }

    [Fact]
    public void VariantNaming_DuplicateModelAndVariantCode_IsRejected()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var now = DateTime.UtcNow;
        ctx.VariantNamings.Add(new VariantNaming { ModelCode = "FJORD-STUDIO", VariantCode = "V1", AssignedCode = "STUDIO-A", CreatedAt = now, UpdatedAt = now });
        ctx.SaveChanges();

        ctx.VariantNamings.Add(new VariantNaming { ModelCode = "FJORD-STUDIO", VariantCode = "V1", AssignedCode = "STUDIO-B", CreatedAt = now, UpdatedAt = now });
        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }
}
