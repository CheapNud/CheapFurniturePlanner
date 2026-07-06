using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

public class Phase3SchemaTests
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
    public void VariantCodeTemplate_RoundTrips()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;

        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ctx.VariantCodeTemplates.Add(new VariantCodeTemplate
        {
            ModelCode = "FJ2",
            VariantCode = "FJ2-__MATERIAL__:Fabric",
            SuggestedCode = "FJ2-FAB",
            Notes = "fabric variant",
            CreatedAt = now,
            UpdatedAt = now
        });
        ctx.SaveChanges();

        var row = ctx.VariantCodeTemplates.Single();
        Assert.Equal("FJ2", row.ModelCode);
        Assert.Equal("FJ2-__MATERIAL__:Fabric", row.VariantCode);
        Assert.Equal("FJ2-FAB", row.SuggestedCode);
        Assert.Equal("fabric variant", row.Notes);
        Assert.Equal(now, row.CreatedAt);
        Assert.Equal(now, row.UpdatedAt);
    }

    [Fact]
    public void ModelStateRecord_RoundTrips_WithDraftDefault()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;

        ctx.ModelStates.Add(new ModelStateRecord { ModelCode = "FJ2" });
        ctx.SaveChanges();

        var row = ctx.ModelStates.Single();
        Assert.Equal("FJ2", row.ModelCode);
        Assert.Equal(TradeItemState.Draft, row.State);
    }

    [Fact]
    public void ModelStateRecord_StoresStateAsString()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;

        ctx.ModelStates.Add(new ModelStateRecord { ModelCode = "FJ2", State = TradeItemState.Active });
        ctx.SaveChanges();

        var row = ctx.ModelStates.Single();
        Assert.Equal(TradeItemState.Active, row.State);
    }

    [Fact]
    public void VariantCodeTemplate_UniqueIndex_RejectsDuplicateModelAndVariantCode()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;

        var now = DateTime.UtcNow;
        ctx.VariantCodeTemplates.Add(new VariantCodeTemplate
        {
            ModelCode = "FJ2",
            VariantCode = "FJ2-__MATERIAL__:Fabric",
            CreatedAt = now,
            UpdatedAt = now
        });
        ctx.SaveChanges();

        ctx.VariantCodeTemplates.Add(new VariantCodeTemplate
        {
            ModelCode = "FJ2",
            VariantCode = "FJ2-__MATERIAL__:Fabric",
            CreatedAt = now,
            UpdatedAt = now
        });

        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void ModelStateRecord_UniqueIndex_RejectsDuplicateModelCode()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;

        ctx.ModelStates.Add(new ModelStateRecord { ModelCode = "FJ2" });
        ctx.SaveChanges();

        ctx.ModelStates.Add(new ModelStateRecord { ModelCode = "FJ2" });

        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }
}
