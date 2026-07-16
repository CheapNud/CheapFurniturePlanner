using CheapFurniturePlanner.Data;
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
        var service = new PartyService(factory);

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
        var service = new PartyService(factory);

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
        var service = new PartyService(factory);
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
        var service = new PartyService(factory);
        var seller = await service.AddSellerAsync("Deletable", 1m);
        var consumer = await service.AddConsumerAsync("Deletable", null);

        await service.DeleteSellerAsync(seller.Id);
        await service.DeleteConsumerAsync(consumer.Id);

        Assert.Empty(await service.SellersAsync());
        Assert.Empty(await service.ConsumersAsync());
    }
}
