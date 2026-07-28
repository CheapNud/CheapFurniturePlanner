using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors ServiceTicketSchemaTests: in-memory SQLite, migrated schema, roles seeded.
public class PlanningSchemaTests
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
            await RoleSeeder.SeedAsync(migrateContext);
        }
        return (new TestDbContextFactory(options), connection);
    }

    [Fact]
    public async Task TripRegion_SetNullOnRegionDelete_AndPromiseRoundTrips()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        var region = new Region { Code = "NORTH", Name = "North route" };
        db.Regions.Add(region);
        var seller = new Seller { Name = "Shop" };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var trip = new Trip { TripCode = "TRP-2026-0001", RegionId = region.Id, State = TripState.Completed, CompletedAt = DateTime.UtcNow };
        db.Trips.Add(trip);
        var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = "BE", PromisedDeliveryDate = new DateTime(2026, 8, 15) };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        db.Regions.Remove(region);
        await db.SaveChangesAsync();

        var loadedTrip = await db.Trips.AsNoTracking().SingleAsync();
        Assert.Null(loadedTrip.RegionId);
        Assert.Equal(TripState.Completed, loadedTrip.State);
        Assert.NotNull(loadedTrip.CompletedAt);
        Assert.Equal(new DateTime(2026, 8, 15), (await db.Orders.AsNoTracking().SingleAsync()).PromisedDeliveryDate);
    }
}
