using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 4b: MaterialHardnessBackfill mirrors DiscountService.BackfillIdentityKeysAsync's role for the
// DiscountRule index (see DiscountServiceTests) - a one-time startup pass (Program.cs, right after
// Database.Migrate()) rewriting every pre-existing null HardnessCode to the row-layer "" sentinel
// on MaterialStock, MaterialProfile and MaterialSupplierTerm, so the unique indexes on all three
// (FurniturePlannerContext's comment on them) actually hold against real data going forward.
public class MaterialHardnessBackfillTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (new TestDbContextFactory(options), connection);
    }

    [Fact]
    public async Task Backfill_RewritesNullHardnessCodeToEmptyString_AcrossAllThreeTables()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        int supplierId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var supplier = new Supplier { Code = "SUPX", Name = "Sup X" };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;

            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FR-1", Amount = 4m, UpdatedAt = DateTime.UtcNow });
            db.MaterialProfiles.Add(new MaterialProfile { Kind = MaterialKind.Cotton, Code = "COT-1", MinimumStock = 5m });
            db.MaterialSupplierTerms.Add(new MaterialSupplierTerm { Kind = MaterialKind.Misc, Code = "GLUE", SupplierId = supplierId, DeliveryTimeDays = 3 });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await MaterialHardnessBackfill.RunAsync(db);
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal("", (await check.MaterialStocks.SingleAsync()).HardnessCode);
        Assert.Equal("", (await check.MaterialProfiles.SingleAsync()).HardnessCode);
        Assert.Equal("", (await check.MaterialSupplierTerms.SingleAsync()).HardnessCode);
    }

    // The disease this fix closes: before it, a race could insert two MaterialStock rows for the
    // same identity (one null, one already ""), silently splitting one balance in two. The backfill
    // must not just rewrite the null row's column - a genuine split like this has to be merged back
    // into one row, additively (Amount summed, the "" row survives, movements untouched).
    [Fact]
    public async Task Backfill_StockSplitAcrossNullAndSentinelRows_MergesAdditivelyOntoSurvivor()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = "", Amount = 4m, UpdatedAt = DateTime.UtcNow });
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = null, Amount = 6m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await MaterialHardnessBackfill.RunAsync(db);
        }

        await using var check = await factory.CreateDbContextAsync();
        var stock = await check.MaterialStocks.SingleAsync();
        Assert.Equal("", stock.HardnessCode);
        Assert.Equal(10m, stock.Amount);
    }

    // Two null rows (no pre-existing "" survivor either) - the first becomes the "" survivor and the
    // rest merge onto it, same additive rule.
    [Fact]
    public async Task Backfill_TwoNullStockRows_MergesOntoOneSurvivor()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FR-1", Amount = 4m, UpdatedAt = DateTime.UtcNow });
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FR-1", Amount = 6m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await MaterialHardnessBackfill.RunAsync(db);
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(1, await check.MaterialStocks.CountAsync());
        var stock = await check.MaterialStocks.SingleAsync();
        Assert.Equal("", stock.HardnessCode);
        Assert.Equal(10m, stock.Amount);
    }

    // No additive quantity on a profile - the newest (highest id) row wins, matching whichever was
    // edited most recently; the older duplicate is discarded rather than merged.
    [Fact]
    public async Task Backfill_ProfileSplitAcrossNullAndSentinelRows_KeepsNewestRow()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialProfiles.Add(new MaterialProfile { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = "", MinimumStock = 5m });
            db.MaterialProfiles.Add(new MaterialProfile { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = null, MinimumStock = 8m });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await MaterialHardnessBackfill.RunAsync(db);
        }

        await using var check = await factory.CreateDbContextAsync();
        var profile = await check.MaterialProfiles.SingleAsync();
        Assert.Equal("", profile.HardnessCode);
        Assert.Equal(8m, profile.MinimumStock); // the newer (higher-id) row's value wins
    }

    // Idempotent: running the backfill again once every row already carries "" is a no-op.
    [Fact]
    public async Task Backfill_NoNullRows_IsNoOp()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = "", Amount = 4m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await MaterialHardnessBackfill.RunAsync(db);
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(1, await check.MaterialStocks.CountAsync());
        Assert.Equal(4m, (await check.MaterialStocks.SingleAsync()).Amount);
    }
}
