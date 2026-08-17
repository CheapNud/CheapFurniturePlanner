using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 1: PartyService is minimal party management for order entry - Sellers and Consumers.
// Harness mirrors MasterAuthoringServiceTests: in-memory SQLite, migrated schema.
public class PartyServiceTests
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
    public async Task AddSeller_PersistsTrimmed_DefaultsAndValidates()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));

        await service.AddSellerAsync("  Alpha  ", 1.2m);

        var sellers = await service.SellersAsync();
        Assert.Contains(sellers, s => s.Name == "Alpha" && s.Multiplier == 1.2m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddSellerAsync("   ", 1m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddSellerAsync("Beta", 0m));
    }

    [Fact]
    public async Task AddUpdateConsumer_RoundTrips()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));

        var consumer = await service.AddConsumerAsync("  Bram  ", "  ");
        Assert.Equal("Bram", consumer.Name);
        Assert.Null(consumer.Contact);

        await service.UpdateConsumerAsync(consumer.Id, "Bram Updated", "  bram@example.com  ");

        var consumers = await service.ConsumersAsync();
        var updated = Assert.Single(consumers);
        Assert.Equal("Bram Updated", updated.Name);
        Assert.Equal("bram@example.com", updated.Contact);
    }

    [Fact]
    public async Task UpdateSeller_EditsInPlace()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var seller = await service.AddSellerAsync("Original", 1m);

        await service.UpdateSellerAsync(seller.Id, "Renamed", 2.5m);

        var sellers = await service.SellersAsync();
        var updated = Assert.Single(sellers);
        Assert.Equal(seller.Id, updated.Id);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(2.5m, updated.Multiplier);
    }

    [Fact]
    public async Task DeleteParty_Unreferenced_Succeeds()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var seller = await service.AddSellerAsync("Deletable", 1m);
        var consumer = await service.AddConsumerAsync("Deletable", null);

        await service.DeleteSellerAsync(seller.Id);
        await service.DeleteConsumerAsync(consumer.Id);

        Assert.Empty(await service.SellersAsync());
        Assert.Empty(await service.ConsumersAsync());
    }

    // Final-review fix 1: MaterialSupplierTerm carries a Restrict FK to Supplier - a supplier
    // referenced only by a term (no orders/reports/model maps) previously escaped the guard and hit
    // SaveChangesAsync as a raw DbUpdateException instead of the friendly snackbar message.
    [Fact]
    public async Task DeleteSupplierAsync_ReferencedByMaterialSupplierTerm_ThrowsInvalidOperation()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var supplier = await service.AddSupplierAsync("MATSUP", "Materials Co");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialSupplierTerms.Add(new MaterialSupplierTerm
            {
                Kind = MaterialKind.Foam, Code = "FM-STD", SupplierId = supplier.Id,
                DeliveryTimeDays = 3, IsPreferred = true,
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteSupplierAsync(supplier.Id));
    }

    // Final-review fix 1: MaterialOrder is also a pre-existing Restrict FK the guard never checked.
    [Fact]
    public async Task DeleteSupplierAsync_ReferencedByMaterialOrder_ThrowsInvalidOperation()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var supplier = await service.AddSupplierAsync("MATSUP", "Materials Co");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialOrders.Add(new MaterialOrder
            {
                Number = "MO-2026-0001", SupplierId = supplier.Id, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteSupplierAsync(supplier.Id));
    }
}
