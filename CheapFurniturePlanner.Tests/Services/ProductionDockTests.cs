using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 3: dock mutations on top of the Task-2 spawn/backfill/cancel service - arrive (by scan or by
// id), undo, trip lifecycle (create/assign/position/depart/delete). All mutations route through
// RequireWarehouseStaffAsync (Admin/Office/Warehouse); trip mutations further require Planning.
public class ProductionDockTests
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

    private static readonly FakeCurrentUser DockUser = new("dock-1", Roles.Warehouse);
    private static readonly FakeCurrentUser MechanicUser = new("wrench", Roles.Mechanic);

    // Seeds a Seller/Consumer/Order directly via EF (no catalogue needed) with the given lines, in
    // the given state. Returns the order id. Mirrors ProductionUnitServiceTests.SeedOrderAsync.
    private static async Task<int> SeedOrderAsync(IDbContextFactory<FurniturePlannerContext> factory,
        OrderState state, params OrderLine[] lines)
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
        foreach (var line in lines) { order.Lines.Add(line); }
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    // Seeds a Placed order with one line of the given quantity and spawns its units. Returns the
    // spawned units (Expected state).
    private static async Task<List<ProductionUnit>> SeedSpawnedUnitsAsync(IDbContextFactory<FurniturePlannerContext> factory,
        ProductionUnitService service, int quantity)
    {
        var orderId = await SeedOrderAsync(factory, OrderState.Placed,
            new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = quantity });
        await service.SpawnForOrderAsync(orderId);
        return await service.UnitsForOrderAsync(orderId);
    }

    [Fact]
    public async Task ArriveByCode_Match_Arrives_And_Stamps()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, DockUser);
        var units = await SeedSpawnedUnitsAsync(factory, service, 1);
        var unitCode = units[0].UnitCode;

        var outcome = await service.ArriveByCodeAsync(unitCode);

        Assert.Equal(ScanOutcome.Arrived, outcome);
        await using var db = await factory.CreateDbContextAsync();
        var reloaded = await db.ProductionUnits.SingleAsync(u => u.UnitCode == unitCode);
        Assert.Equal(ProductionUnitState.Arrived, reloaded.State);
        Assert.NotNull(reloaded.ArrivedAt);
    }

    [Fact]
    public async Task ArriveByCode_Twice_ReportsAlreadyArrived()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, DockUser);
        var units = await SeedSpawnedUnitsAsync(factory, service, 1);
        var unitCode = units[0].UnitCode;
        await service.ArriveByCodeAsync(unitCode);

        var outcome = await service.ArriveByCodeAsync(unitCode);

        Assert.Equal(ScanOutcome.AlreadyArrived, outcome);
        await using var db = await factory.CreateDbContextAsync();
        var reloaded = await db.ProductionUnits.SingleAsync(u => u.UnitCode == unitCode);
        Assert.Equal(ProductionUnitState.Arrived, reloaded.State);
    }

    [Fact]
    public async Task ArriveByCode_Unknown_ReportsUnknown()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, DockUser);
        var units = await SeedSpawnedUnitsAsync(factory, service, 1);

        var bogusOutcome = await service.ArriveByCodeAsync("NOT-A-REAL-CODE");
        Assert.Equal(ScanOutcome.Unknown, bogusOutcome);

        var orderId = units[0].OrderId;
        await service.CancelForOrderAsync(orderId);
        var cancelledOutcome = await service.ArriveByCodeAsync(units[0].UnitCode);
        Assert.Equal(ScanOutcome.Unknown, cancelledOutcome);
    }

    [Fact]
    public async Task UndoArrive_OnlyWhileUnassigned_StoresNote()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, DockUser);
        var units = await SeedSpawnedUnitsAsync(factory, service, 1);
        var unitId = units[0].Id;
        await service.ArriveAsync(unitId);
        var trip = await service.CreateTripAsync();
        await service.AssignToTripAsync(trip.Id, unitId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UndoArriveAsync(unitId, "oops"));

        await service.ReleaseFromTripAsync(unitId);
        await service.UndoArriveAsync(unitId, "wrong item scanned");

        await using var db = await factory.CreateDbContextAsync();
        var reloaded = await db.ProductionUnits.SingleAsync(u => u.Id == unitId);
        Assert.Equal(ProductionUnitState.Expected, reloaded.State);
        Assert.Null(reloaded.ArrivedAt);
        Assert.Equal("wrong item scanned", reloaded.ReviewNote);
    }

    [Fact]
    public async Task Mechanic_CannotWorkTheDock()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var seedService = new ProductionUnitService(factory, DockUser);
        var units = await SeedSpawnedUnitsAsync(factory, seedService, 1);
        var mechanicService = new ProductionUnitService(factory, MechanicUser);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mechanicService.ArriveAsync(units[0].Id));
    }

    [Fact]
    public async Task Trip_Lifecycle_AssignPositionDepart()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, DockUser);
        var units = await SeedSpawnedUnitsAsync(factory, service, 2);
        await service.ArriveAsync(units[0].Id);
        await service.ArriveAsync(units[1].Id);

        var trip = await service.CreateTripAsync();
        Assert.Equal($"TRP-{DateTime.UtcNow.Year}-0001", trip.TripCode);

        await service.AssignToTripAsync(trip.Id, units[0].Id);
        await service.AssignToTripAsync(trip.Id, units[1].Id);
        await service.SetLoadPositionAsync(units[0].Id, 1);
        await service.SetLoadPositionAsync(units[1].Id, 2);

        await service.DepartAsync(trip.Id);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var reloadedUnits = await db.ProductionUnits.Where(u => u.Id == units[0].Id || u.Id == units[1].Id).ToListAsync();
            Assert.All(reloadedUnits, u => Assert.Equal(ProductionUnitState.Delivered, u.State));
            var reloadedTrip = await db.Trips.SingleAsync(t => t.Id == trip.Id);
            Assert.Equal(TripState.Departed, reloadedTrip.State);
            Assert.NotNull(reloadedTrip.DepartedAt);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateTripAsync(trip.Id, null, "Truck 1", "Driver 1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignToTripAsync(trip.Id, units[0].Id));
    }

    [Fact]
    public async Task Assign_RequiresArrivedUnassigned()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, DockUser);
        var units = await SeedSpawnedUnitsAsync(factory, service, 2);
        var trip = await service.CreateTripAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignToTripAsync(trip.Id, units[0].Id));

        await service.ArriveAsync(units[0].Id);
        await service.AssignToTripAsync(trip.Id, units[0].Id);
        var otherTrip = await service.CreateTripAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignToTripAsync(otherTrip.Id, units[0].Id));
    }

    [Fact]
    public async Task Depart_EmptyTrip_Throws_And_DeleteOnlyEmptyPlanning()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, DockUser);
        var units = await SeedSpawnedUnitsAsync(factory, service, 1);
        var trip = await service.CreateTripAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DepartAsync(trip.Id));

        await service.ArriveAsync(units[0].Id);
        await service.AssignToTripAsync(trip.Id, units[0].Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteTripAsync(trip.Id));

        await service.ReleaseFromTripAsync(units[0].Id);
        await service.DeleteTripAsync(trip.Id);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.Trips.AnyAsync(t => t.Id == trip.Id));
    }

    // Regression: CreateTripAsync must not derive the next trip number from a COUNT of existing
    // trips with the year prefix - deleting a mid-sequence trip then shrinks the count, causing the
    // next created trip to reuse a TripCode that still exists and blow the unique index.
    [Fact]
    public async Task CreateTrip_AfterDeletingFirstOfTwo_SkipsToNextFreeNumber()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, DockUser);

        var tripA = await service.CreateTripAsync();
        var tripB = await service.CreateTripAsync();
        Assert.Equal($"TRP-{DateTime.UtcNow.Year}-0001", tripA.TripCode);
        Assert.Equal($"TRP-{DateTime.UtcNow.Year}-0002", tripB.TripCode);

        await service.DeleteTripAsync(tripA.Id);

        var tripC = await service.CreateTripAsync();

        Assert.Equal($"TRP-{DateTime.UtcNow.Year}-0003", tripC.TripCode);
    }
}
