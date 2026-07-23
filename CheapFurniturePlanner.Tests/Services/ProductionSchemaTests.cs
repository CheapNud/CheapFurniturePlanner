using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors ServiceTicketSchemaTests: in-memory SQLite, migrated schema, roles seeded.
public class ProductionSchemaTests
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
    public async Task Unit_AndTrip_RoundTrip_WithSetNullOnTripDelete()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        int unitId;
        int orderId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var seller = new Seller { Name = "Shop" };
            var consumer = new Consumer { Name = "Jansen" };
            db.Sellers.Add(seller);
            db.Consumers.Add(consumer);
            await db.SaveChangesAsync();
            var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = "BE" };
            order.Lines.Add(new OrderLine { Kind = OrderLineKind.ConfiguredElement, DisplayIndex = 0, Quantity = 1, UnitPrice = 100m, LineTotal = 100m });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
            var trip = new Trip { TripCode = "TRP-2026-0001" };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            var unit = new ProductionUnit
            {
                OrderId = order.Id,
                OrderLineId = order.Lines[0].Id,
                SequenceNumber = 1,
                UnitCode = "ORD-2026-0001-1-1",
                State = ProductionUnitState.Arrived,
                TripId = trip.Id,
                LoadPosition = 4,
                CreatedAt = DateTime.UtcNow,
            };
            db.ProductionUnits.Add(unit);
            await db.SaveChangesAsync();
            unitId = unit.Id;
            db.Trips.Remove(trip);
            await db.SaveChangesAsync();
        }
        await using (var verify = await factory.CreateDbContextAsync())
        {
            var loaded = await verify.ProductionUnits.SingleAsync(u => u.Id == unitId);
            Assert.Null(loaded.TripId); // SetNull, not cascade: deleting a trip releases its units
            Assert.Equal(ProductionUnitState.Arrived, loaded.State);
            var line = await verify.OrderLines.SingleAsync();
            Assert.True(line.DeliverToWarehouse); // CLR initializer

            // Genuine DB-default assertion: insert a row via raw SQL bypassing EF's CLR value,
            // omitting DeliverToWarehouse entirely, and confirm the column-level default kicks in.
            await verify.Database.ExecuteSqlRawAsync(
                "INSERT INTO OrderLines (OrderId, DisplayIndex, Kind, SelectionsJson, Quantity, UnitPrice, LineTotal, DiscountPercent, DiscountIsManual) VALUES ({0}, 7, 0, '{{}}', 1, 0, 0, 0, 0)",
                orderId);
            var rawInsertedLine = await verify.OrderLines.SingleAsync(l => l.DisplayIndex == 7);
            Assert.True(rawInsertedLine.DeliverToWarehouse); // DB column default, not the CLR initializer
        }
    }

    [Fact]
    public async Task DuplicateUnitCode_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop" };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = "BE" };
        order.Lines.Add(new OrderLine { Kind = OrderLineKind.ConfiguredElement, DisplayIndex = 0, Quantity = 1, UnitPrice = 100m, LineTotal = 100m });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        db.ProductionUnits.Add(new ProductionUnit { OrderId = order.Id, OrderLineId = order.Lines[0].Id, SequenceNumber = 1, UnitCode = "DUP-1-1", CreatedAt = DateTime.UtcNow });
        db.ProductionUnits.Add(new ProductionUnit { OrderId = order.Id, OrderLineId = order.Lines[0].Id, SequenceNumber = 2, UnitCode = "DUP-1-1", CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
