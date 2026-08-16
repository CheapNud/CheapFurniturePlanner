using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 1: the SP-3 stock audit log itself - MaterialMovement, plus the two demand/supply-side
// entities (MaterialProfile, MaterialSupplierTerm) that ride the same migration with no service
// yet (later tasks wire those). Per-site movement-write behavior (Receipt/Backflush/BackflushUndo/
// Adjustment) is pinned as appends to MaterialOrderServiceTests/ProductionUnitServiceTests/
// MaterialNeedsServiceTests, next to the mutation each write accompanies - this file covers the
// movement row's own field shape plus the two new entities' migration-level invariants.
public class MaterialMovementTests
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

    private static readonly FakeCurrentUser OfficeUser = new("office-1", Roles.Office);

    private static async Task<int> SeedSupplierAsync(IDbContextFactory<FurniturePlannerContext> factory, string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = code, Name = code };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    [Fact]
    public async Task Receive_WritesMovement_WithFullFieldShape()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId,
            [new MaterialOrderLine { Kind = MaterialKind.Foam, Code = "F-100", HardnessCode = "H35", QuantityOrdered = 20m }]);
        var line = order.Lines[0];
        await materials.SendAsync(order.Id);
        var before = DateTime.UtcNow;

        await materials.ReceiveAsync(order.Id, line.Id, 6m);

        await using var db = await factory.CreateDbContextAsync();
        var movement = await db.MaterialMovements.SingleAsync();
        Assert.Equal(MaterialKind.Foam, movement.Kind);
        Assert.Equal("F-100", movement.Code);
        Assert.Equal("H35", movement.HardnessCode);
        Assert.Equal(6m, movement.Quantity);
        Assert.Equal(MaterialMovementType.Receipt, movement.Type);
        Assert.Equal(order.Number, movement.Reference);
        Assert.Equal("office-1", movement.UserId);
        Assert.True(movement.OccurredAt >= before);
    }

    [Fact]
    public async Task AdjustStock_RaiseWritesPositiveDelta_LowerWritesNegativeDelta_ReferenceNull()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

        await service.AdjustStockAsync(MaterialKind.Foam, "FM-STD", "H35", 12m); // 0 -> 12, delta +12 (raise)
        await service.AdjustStockAsync(MaterialKind.Foam, "FM-STD", "H35", -4m); // 12 -> -4, delta -16 (lower)

        await using var db = await factory.CreateDbContextAsync();
        var movements = await db.MaterialMovements.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m =>
        {
            Assert.Equal(MaterialMovementType.Adjustment, m.Type);
            Assert.Null(m.Reference);
            Assert.Equal("office-1", m.UserId);
        });
        Assert.Equal(12m, movements[0].Quantity);
        Assert.Equal(-16m, movements[1].Quantity);
    }

    [Fact]
    public async Task MaterialProfile_UniqueIndex_RejectsDuplicateIdentity()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        db.MaterialProfiles.Add(new MaterialProfile { Kind = MaterialKind.Foam, Code = "FM-STD", HardnessCode = "H35", MinimumStock = 10m });
        await db.SaveChangesAsync();

        db.MaterialProfiles.Add(new MaterialProfile { Kind = MaterialKind.Foam, Code = "FM-STD", HardnessCode = "H35", MinimumStock = 20m });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MaterialSupplierTerm_UniqueIndex_RejectsDuplicateIdentity()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        await using var db = await factory.CreateDbContextAsync();
        // SQLite treats every NULL in a unique index as distinct from every other NULL, so a
        // meaningful duplicate-rejection check needs a non-null HardnessCode here (same quirk
        // MaterialStock already lives with - two null-hardness rows never collide either).
        db.MaterialSupplierTerms.Add(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "FM-STD", HardnessCode = "H35", SupplierId = supplierId, DeliveryTimeDays = 7, IsPreferred = true });
        await db.SaveChangesAsync();

        db.MaterialSupplierTerms.Add(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "FM-STD", HardnessCode = "H35", SupplierId = supplierId, DeliveryTimeDays = 14 });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
