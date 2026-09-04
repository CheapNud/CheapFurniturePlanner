using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 2 schema: MaterialStock's (Kind, Code, HardnessCode) identity and a MaterialOrder/Line
// round-trip. Harness mirrors PurchasingSchemaTests: in-memory SQLite, migrated schema.
public class MaterialEntitiesTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static async Task<(IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection)> NewFactoryAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        await using (var migrateContext = new FurniturePlannerContext(options))
        {
            await migrateContext.Database.MigrateAsync();
        }
        return (new TestDbContextFactory(options), connection);
    }

    [Fact]
    public async Task MaterialStock_DuplicateKindCodeHardness_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        db.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "F-100", HardnessCode = "H35", Amount = 10m, UpdatedAt = DateTime.UtcNow });
        db.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "F-100", HardnessCode = "H35", Amount = 5m, UpdatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // Task 4b: pins the mechanism the row-layer "" sentinel relies on - the unique index itself was
    // never broken (two equal non-null values always collided), only the application ever wrote null
    // for a non-Foam material. This is what every "" write now lands on and collides against; the
    // service-level tests (MaterialPlanningServiceTests, MaterialOrderServiceTests) are what actually
    // prove the fix, since two raw nulls inserted here would still never collide - that's unfixable
    // without a schema change, and this task makes none.
    [Fact]
    public async Task MaterialStock_DuplicateEmptyStringHardness_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        db.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = "", Amount = 10m, UpdatedAt = DateTime.UtcNow });
        db.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = "", Amount = 5m, UpdatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MaterialProfile_DuplicateEmptyStringHardness_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        db.Add(new MaterialProfile { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = "", MinimumStock = 5m });
        db.Add(new MaterialProfile { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = "", MinimumStock = 8m });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MaterialSupplierTerm_DuplicateEmptyStringHardness_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = "SUPX", Name = "Sup X" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        db.Add(new MaterialSupplierTerm { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = "", SupplierId = supplier.Id, DeliveryTimeDays = 3 });
        db.Add(new MaterialSupplierTerm { Kind = MaterialKind.Frame, Code = "FR-1", HardnessCode = "", SupplierId = supplier.Id, DeliveryTimeDays = 7 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MaterialStock_SameCodeDifferentHardness_BothAllowed()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        db.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "F-100", HardnessCode = "H35", Amount = 10m, UpdatedAt = DateTime.UtcNow });
        db.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "F-100", HardnessCode = "H45", Amount = 3m, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Set<MaterialStock>().CountAsync());
    }

    [Fact]
    public async Task MaterialOrder_RoundTrip_WithLines()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        int orderId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var supplier = new Supplier { Code = "WOODWORKS", Name = "Woodworks Fine" };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();

            var order = new MaterialOrder
            {
                Number = "MO-2026-0001",
                SupplierId = supplier.Id,
                CreatedByUserId = "office-1",
                CreatedAt = DateTime.UtcNow,
            };
            order.Lines.Add(new MaterialOrderLine { Kind = MaterialKind.Foam, Code = "F-100", HardnessCode = "H35", QuantityOrdered = 20m });
            order.Lines.Add(new MaterialOrderLine { Kind = MaterialKind.Fabric, Code = "FAB-9", DisplayName = "Blue linen", QuantityOrdered = 15m });
            db.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var order = await db.Set<MaterialOrder>().Include(o => o.Lines).SingleAsync(o => o.Id == orderId);
            Assert.Equal(MaterialOrderState.Draft, order.State);
            Assert.Equal(2, order.Lines.Count);
            Assert.Contains(order.Lines, l => l.Kind == MaterialKind.Foam && l.HardnessCode == "H35" && l.QuantityOrdered == 20m);
            Assert.Contains(order.Lines, l => l.Kind == MaterialKind.Fabric && l.DisplayName == "Blue linen");
        }
    }

    [Fact]
    public async Task MaterialOrder_DuplicateNumber_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = "WOODWORKS", Name = "Woodworks Fine" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        db.Add(new MaterialOrder { Number = "MO-2026-0001", SupplierId = supplier.Id, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow });
        db.Add(new MaterialOrder { Number = "MO-2026-0001", SupplierId = supplier.Id, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SupplierModelMap_NullSupplierId_IsAllowed()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = "INHOUSE" });
        await db.SaveChangesAsync();
        var map = await db.SupplierModelMaps.SingleAsync();
        Assert.Null(map.SupplierId);
        Assert.Equal("INHOUSE", map.ModelCode);
    }
}
