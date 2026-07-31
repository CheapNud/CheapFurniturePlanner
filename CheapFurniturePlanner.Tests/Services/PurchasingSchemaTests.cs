using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors ProductionSchemaTests: in-memory SQLite, migrated schema, roles seeded.
public class PurchasingSchemaTests
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
    public async Task ModelMap_PoAndDelivery_RoundTrip_WithSetNullOnEitherDelete()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        int unitId, supplierOrderId, supplierDeliveryId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var seller = new Seller { Name = "Shop" };
            var consumer = new Consumer { Name = "Jansen" };
            var supplier = new Supplier { Code = "WOODWORKS", Name = "Woodworks Fine" };
            db.Sellers.Add(seller);
            db.Consumers.Add(consumer);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();

            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = supplier.Id, ModelCode = "FJORD" });

            var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = "BE" };
            order.Lines.Add(new OrderLine { Kind = OrderLineKind.ConfiguredElement, DisplayIndex = 0, ModelCode = "FJORD", Quantity = 1, UnitPrice = 100m, LineTotal = 100m });
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            var supplierOrder = new SupplierOrder { PoNumber = "PO-2026-0001", SupplierId = supplier.Id, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow };
            var supplierDelivery = new SupplierDelivery { SupplierId = supplier.Id, Reference = "DN-0001", CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow };
            db.SupplierOrders.Add(supplierOrder);
            db.SupplierDeliveries.Add(supplierDelivery);
            await db.SaveChangesAsync();

            var unit = new ProductionUnit
            {
                OrderId = order.Id,
                OrderLineId = order.Lines[0].Id,
                SequenceNumber = 1,
                UnitCode = "ORD-2026-0001-1-1",
                State = ProductionUnitState.Expected,
                SupplierOrderId = supplierOrder.Id,
                SupplierDeliveryId = supplierDelivery.Id,
                CreatedAt = DateTime.UtcNow,
            };
            db.ProductionUnits.Add(unit);
            await db.SaveChangesAsync();
            unitId = unit.Id;
            supplierOrderId = supplierOrder.Id;
            supplierDeliveryId = supplierDelivery.Id;
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var map = await db.SupplierModelMaps.SingleAsync();
            Assert.Equal("FJORD", map.ModelCode);
            var unit = await db.ProductionUnits.SingleAsync(u => u.Id == unitId);
            Assert.Equal(supplierOrderId, unit.SupplierOrderId);
            Assert.Equal(supplierDeliveryId, unit.SupplierDeliveryId);
        }

        // PO-line-are-units: deleting the PO releases the unit, not the other way around.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SupplierOrders.Remove(await db.SupplierOrders.SingleAsync(o => o.Id == supplierOrderId));
            await db.SaveChangesAsync();
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var unit = await db.ProductionUnits.SingleAsync(u => u.Id == unitId);
            Assert.Null(unit.SupplierOrderId); // SetNull, not cascade: deleting a PO releases its units
            Assert.Equal(supplierDeliveryId, unit.SupplierDeliveryId); // untouched by the PO deletion
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SupplierDeliveries.Remove(await db.SupplierDeliveries.SingleAsync(d => d.Id == supplierDeliveryId));
            await db.SaveChangesAsync();
        }
        await using (var verify = await factory.CreateDbContextAsync())
        {
            var unit = await verify.ProductionUnits.SingleAsync(u => u.Id == unitId);
            Assert.Null(unit.SupplierDeliveryId); // SetNull: deleting the announcement releases its units too
        }
    }

    [Fact]
    public async Task DuplicateModelCode_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = "WOODWORKS", Name = "Woodworks Fine" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = supplier.Id, ModelCode = "FJORD" });
        db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = supplier.Id, ModelCode = "FJORD" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateSupplierReference_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = "WOODWORKS", Name = "Woodworks Fine" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        db.SupplierDeliveries.Add(new SupplierDelivery { SupplierId = supplier.Id, Reference = "DN-0001", CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow });
        db.SupplierDeliveries.Add(new SupplierDelivery { SupplierId = supplier.Id, Reference = "DN-0001", CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
