using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 3: region-filtered assignable pool, the PromiseMissed comparison, and promised-delivery-date
// editing (allowed on Draft and Placed, blocked once Cancelled). Harness mirrors
// ProductionUnitServiceTests: in-memory SQLite, migrated schema, FakeCurrentUser.
public class PlanningQueriesTests
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

    // Seeds a Seller/Consumer/Order directly via EF (no catalogue needed), optionally with a
    // delivery address pointed at the given region, in the given state, with one line of the given
    // quantity. Returns the order id.
    private static async Task<int> SeedOrderAsync(IDbContextFactory<FurniturePlannerContext> factory,
        OrderState state, int? regionId, int quantity = 1)
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop", Multiplier = 1m };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var order = new Order
        {
            OrderNumber = $"ORD-2026-{await db.Orders.CountAsync() + 1:D4}",
            SellerId = seller.Id,
            ConsumerId = consumer.Id,
            MarketCode = "BE",
            State = state,
        };
        if (regionId is not null)
        {
            var address = new Address { Street = "Dockweg", Number = "1", PostalCode = "1000", City = "Brussel", RegionId = regionId };
            db.Addresses.Add(address);
            await db.SaveChangesAsync();
            order.DeliveryAddressId = address.Id;
        }
        order.Lines.Add(new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = quantity });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task AssignablePool_FiltersByRegion_SoftNullMeansAll()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Regions.Add(new Region { Code = "NORTH", Name = "North" });
            await db.SaveChangesAsync();
        }
        var northRegionId = await (await factory.CreateDbContextAsync()).Regions.Select(r => r.Id).SingleAsync();
        var units = new ProductionUnitService(factory, OfficeUser);
        var northOrderId = await SeedOrderAsync(factory, OrderState.Placed, northRegionId);
        var noAddressOrderId = await SeedOrderAsync(factory, OrderState.Placed, regionId: null);
        await units.SpawnForOrderAsync(northOrderId);
        await units.SpawnForOrderAsync(noAddressOrderId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var unit in await db.ProductionUnits.ToListAsync())
            {
                unit.State = ProductionUnitState.Arrived;
            }
            await db.SaveChangesAsync();
        }

        var northOnly = await units.AssignableUnitsAsync(northRegionId);
        var all = await units.AssignableUnitsAsync();

        Assert.Single(northOnly);
        Assert.Equal(northOrderId, northOnly[0].OrderId);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void PromiseMissed_BoundaryMatrix()
    {
        Assert.False(ProductionUnitService.PromiseMissed(null, null));
        Assert.False(ProductionUnitService.PromiseMissed(new DateTime(2026, 7, 20), null));
        Assert.False(ProductionUnitService.PromiseMissed(null, new DateTime(2026, 7, 20)));
        Assert.False(ProductionUnitService.PromiseMissed(new DateTime(2026, 7, 20), new DateTime(2026, 7, 20)));
        Assert.True(ProductionUnitService.PromiseMissed(new DateTime(2026, 7, 20), new DateTime(2026, 7, 21)));
    }

    [Fact]
    public async Task SetPromise_DraftAndPlacedOk_CancelledThrows()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var units = new ProductionUnitService(factory, OfficeUser);
        var orders = new OrderEntryService(factory, new DbCatalogueSource(factory), new PinnedCatalogueProvider(factory), units);
        var orderId = await SeedOrderAsync(factory, OrderState.Draft, regionId: null);

        var promisedDate = new DateTime(2026, 8, 1);
        await orders.SetPromisedDeliveryDateAsync(orderId, promisedDate);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(promisedDate, (await db.Orders.SingleAsync(o => o.Id == orderId)).PromisedDeliveryDate);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.Orders.SingleAsync(o => o.Id == orderId)).State = OrderState.Placed;
            await db.SaveChangesAsync();
        }
        var reschedule = new DateTime(2026, 8, 5);
        await orders.SetPromisedDeliveryDateAsync(orderId, reschedule);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(reschedule, (await db.Orders.SingleAsync(o => o.Id == orderId)).PromisedDeliveryDate);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.Orders.SingleAsync(o => o.Id == orderId)).State = OrderState.Cancelled;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orders.SetPromisedDeliveryDateAsync(orderId, new DateTime(2026, 8, 10)));
    }
}
