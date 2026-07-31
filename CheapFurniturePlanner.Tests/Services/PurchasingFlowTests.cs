using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 3: auto-completing a Sent purchase order when its last non-cancelled unit arrives (hooked
// into both arrival paths and the order-cancel cascade), and supplier delivery announcements
// (attach/detach/delete guards, IsOverdue, the ListUnitsAsync announcement filter). Harness mirrors
// PurchasingServiceTests/ProductionUnitServiceTests: in-memory SQLite, migrated schema,
// FakeCurrentUser, direct-EF seeding.
public class PurchasingFlowTests
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
    private static readonly FakeCurrentUser WarehouseUser = new("wh-1", Roles.Warehouse);

    private static async Task<int> SeedSupplierAsync(IDbContextFactory<FurniturePlannerContext> factory, string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = code, Name = code };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    private static async Task<int> SeedSupplierOrderAsync(IDbContextFactory<FurniturePlannerContext> factory, int supplierId, string poNumber, SupplierOrderState state)
    {
        await using var db = await factory.CreateDbContextAsync();
        var order = new SupplierOrder { PoNumber = poNumber, SupplierId = supplierId, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow, State = state };
        db.SupplierOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    // Seeds a Seller/Consumer/placed Order with one warehouse-bound line and one ProductionUnit,
    // optionally pre-linked to a purchase order and/or given a starting state - the minimal chain
    // needed for arrival/cancellation hooks without going through the sweep.
    private static async Task<(int OrderId, int UnitId)> SeedUnitAsync(
        IDbContextFactory<FurniturePlannerContext> factory,
        int? supplierOrderId = null,
        ProductionUnitState state = ProductionUnitState.Expected,
        int? supplierDeliveryId = null)
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
            State = OrderState.Placed,
        };
        order.Lines.Add(new OrderLine
        {
            DisplayIndex = 0,
            Kind = OrderLineKind.ConfiguredElement,
            ModelCode = "FJORD",
            DeliverToWarehouse = true,
            Quantity = 1,
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var line = order.Lines[0];
        var unit = new ProductionUnit
        {
            OrderId = order.Id,
            OrderLineId = line.Id,
            SequenceNumber = 1,
            UnitCode = $"{order.OrderNumber}-1-1",
            State = state,
            SupplierOrderId = supplierOrderId,
            SupplierDeliveryId = supplierDeliveryId,
            ArrivedAt = state == ProductionUnitState.Arrived ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
        };
        db.ProductionUnits.Add(unit);
        await db.SaveChangesAsync();
        return (order.Id, unit.Id);
    }

    // -- auto-completion --

    [Fact]
    public async Task ArriveAsync_LastNonCancelledUnit_CompletesSentPO()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var poId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0001", SupplierOrderState.Sent);
        // A cancelled unit on the same PO must not block completion.
        await SeedUnitAsync(factory, supplierOrderId: poId, state: ProductionUnitState.Cancelled);
        var (_, expectedUnitId) = await SeedUnitAsync(factory, supplierOrderId: poId, state: ProductionUnitState.Expected);
        var units = new ProductionUnitService(factory, WarehouseUser);
        var purchasing = new PurchasingService(factory, OfficeUser);

        await units.ArriveAsync(expectedUnitId);

        var order = await purchasing.GetOrderAsync(poId);
        Assert.Equal(SupplierOrderState.Completed, order!.State);
    }

    [Fact]
    public async Task ArriveByCodeAsync_LastExpectedUnit_CompletesSentPO()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var poId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0001", SupplierOrderState.Sent);
        var (_, unitId) = await SeedUnitAsync(factory, supplierOrderId: poId, state: ProductionUnitState.Expected);
        var units = new ProductionUnitService(factory, WarehouseUser);
        var purchasing = new PurchasingService(factory, OfficeUser);
        string unitCode;
        await using (var db = await factory.CreateDbContextAsync())
        {
            unitCode = (await db.ProductionUnits.SingleAsync(u => u.Id == unitId)).UnitCode;
        }

        var outcome = await units.ArriveByCodeAsync(unitCode);

        Assert.Equal(ScanOutcome.Arrived, outcome);
        var order = await purchasing.GetOrderAsync(poId);
        Assert.Equal(SupplierOrderState.Completed, order!.State);
    }

    [Fact]
    public async Task ArriveAsync_NotLastExpectedUnit_LeavesSentPOOpen()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var poId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0001", SupplierOrderState.Sent);
        var (_, unitOneId) = await SeedUnitAsync(factory, supplierOrderId: poId, state: ProductionUnitState.Expected);
        await SeedUnitAsync(factory, supplierOrderId: poId, state: ProductionUnitState.Expected);
        var units = new ProductionUnitService(factory, WarehouseUser);
        var purchasing = new PurchasingService(factory, OfficeUser);

        await units.ArriveAsync(unitOneId);

        var order = await purchasing.GetOrderAsync(poId);
        Assert.Equal(SupplierOrderState.Sent, order!.State);
    }

    [Fact]
    public async Task CancelForOrder_ReleasesDraftLink_KeepsSentLink_TriggersCompletion()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var draftPoId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0001", SupplierOrderState.Draft);
        var sentPoId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0002", SupplierOrderState.Sent);
        var (draftOrderId, draftUnitId) = await SeedUnitAsync(factory, supplierOrderId: draftPoId, state: ProductionUnitState.Expected);
        var (sentOrderId, sentUnitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoId, state: ProductionUnitState.Expected);
        // Second unit on the Sent PO is already past Expected, so cancelling the other order's
        // unit should be the one that tips the PO into Completed.
        await SeedUnitAsync(factory, supplierOrderId: sentPoId, state: ProductionUnitState.Arrived);
        var units = new ProductionUnitService(factory, WarehouseUser);
        var purchasing = new PurchasingService(factory, OfficeUser);

        await units.CancelForOrderAsync(draftOrderId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var draftUnit = await db.ProductionUnits.SingleAsync(u => u.Id == draftUnitId);
            Assert.Equal(ProductionUnitState.Cancelled, draftUnit.State);
            Assert.Null(draftUnit.SupplierOrderId);
        }
        var draftPo = await purchasing.GetOrderAsync(draftPoId);
        Assert.Equal(SupplierOrderState.Draft, draftPo!.State);

        await units.CancelForOrderAsync(sentOrderId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var sentUnit = await db.ProductionUnits.SingleAsync(u => u.Id == sentUnitId);
            Assert.Equal(ProductionUnitState.Cancelled, sentUnit.State);
            Assert.Equal(sentPoId, sentUnit.SupplierOrderId);
        }
        var sentPo = await purchasing.GetOrderAsync(sentPoId);
        Assert.Equal(SupplierOrderState.Completed, sentPo!.State);
    }

    // -- announcements --

    [Fact]
    public async Task AttachToAnnouncement_Guards()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        var supplierB = await SeedSupplierAsync(factory, "SUPB");
        var draftPoId = await SeedSupplierOrderAsync(factory, supplierA, "PO-2026-0001", SupplierOrderState.Draft);
        var sentPoA = await SeedSupplierOrderAsync(factory, supplierA, "PO-2026-0002", SupplierOrderState.Sent);
        var sentPoB = await SeedSupplierOrderAsync(factory, supplierB, "PO-2026-0003", SupplierOrderState.Sent);
        var (_, draftUnitId) = await SeedUnitAsync(factory, supplierOrderId: draftPoId);
        var (_, wrongSupplierUnitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoB);
        var (_, arrivedUnitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoA, state: ProductionUnitState.Arrived);
        var (_, goodUnitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoA);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var announcement = await purchasing.CreateAnnouncementAsync(supplierA, "DN-0001", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.AttachToAnnouncementAsync(announcement.Id, draftUnitId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.AttachToAnnouncementAsync(announcement.Id, wrongSupplierUnitId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.AttachToAnnouncementAsync(announcement.Id, arrivedUnitId));

        await purchasing.AttachToAnnouncementAsync(announcement.Id, goodUnitId);
        var secondAnnouncement = await purchasing.CreateAnnouncementAsync(supplierA, "DN-0002", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.AttachToAnnouncementAsync(secondAnnouncement.Id, goodUnitId));

        var attached = await purchasing.ListAnnouncementsAsync(supplierA);
        var reloaded = attached.Single(a => a.Id == announcement.Id);
        Assert.Equal(goodUnitId, Assert.Single(reloaded.Units).Id);
    }

    [Fact]
    public async Task DetachFromAnnouncement_OnlyUnarrived()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var sentPoId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0001", SupplierOrderState.Sent);
        var (_, unitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoId);
        var (_, arrivingUnitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoId);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var units = new ProductionUnitService(factory, WarehouseUser);
        var announcement = await purchasing.CreateAnnouncementAsync(supplierId, "DN-0001", null);
        await purchasing.AttachToAnnouncementAsync(announcement.Id, unitId);
        await purchasing.AttachToAnnouncementAsync(announcement.Id, arrivingUnitId);

        await purchasing.DetachFromAnnouncementAsync(unitId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null((await db.ProductionUnits.SingleAsync(u => u.Id == unitId)).SupplierDeliveryId);
        }

        await units.ArriveAsync(arrivingUnitId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.DetachFromAnnouncementAsync(arrivingUnitId));
    }

    [Fact]
    public async Task DeleteAnnouncement_OnlyEmpty()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var sentPoId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0001", SupplierOrderState.Sent);
        var (_, unitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoId);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var announcement = await purchasing.CreateAnnouncementAsync(supplierId, "DN-0001", null);
        await purchasing.AttachToAnnouncementAsync(announcement.Id, unitId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.DeleteAnnouncementAsync(announcement.Id));

        await purchasing.DetachFromAnnouncementAsync(unitId);
        await purchasing.DeleteAnnouncementAsync(announcement.Id);

        var remaining = await purchasing.ListAnnouncementsAsync(supplierId, openOnly: false);
        Assert.DoesNotContain(remaining, a => a.Id == announcement.Id);
    }

    [Fact]
    public void IsOverdue_Boundaries()
    {
        var expectedUnit = new ProductionUnit { UnitCode = "X", State = ProductionUnitState.Expected };
        var arrivedUnit = new ProductionUnit { UnitCode = "Y", State = ProductionUnitState.Arrived };

        Assert.False(PurchasingService.IsOverdue(new SupplierDelivery
        {
            Reference = "DN-1", CreatedByUserId = "u", CreatedAt = DateTime.UtcNow, ExpectedDate = null, Units = [expectedUnit],
        }));
        Assert.False(PurchasingService.IsOverdue(new SupplierDelivery
        {
            Reference = "DN-2", CreatedByUserId = "u", CreatedAt = DateTime.UtcNow, ExpectedDate = DateTime.UtcNow.Date.AddDays(1), Units = [expectedUnit],
        }));
        Assert.True(PurchasingService.IsOverdue(new SupplierDelivery
        {
            Reference = "DN-3", CreatedByUserId = "u", CreatedAt = DateTime.UtcNow, ExpectedDate = DateTime.UtcNow.Date.AddDays(-1), Units = [expectedUnit],
        }));
        Assert.False(PurchasingService.IsOverdue(new SupplierDelivery
        {
            Reference = "DN-4", CreatedByUserId = "u", CreatedAt = DateTime.UtcNow, ExpectedDate = DateTime.UtcNow.Date.AddDays(-1), Units = [arrivedUnit],
        }));
    }

    // Regression: cancelling a unit's order while it's still parked on an announcement used to
    // leave SupplierDeliveryId dangling - the announcement would then look perpetually open/overdue
    // for a unit that can never arrive.
    [Fact]
    public async Task CancelForOrder_ClearsAnnouncementLink()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var sentPoId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0001", SupplierOrderState.Sent);
        var (orderId, unitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoId, state: ProductionUnitState.Expected);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var units = new ProductionUnitService(factory, WarehouseUser);
        var announcement = await purchasing.CreateAnnouncementAsync(supplierId, "DN-0001", null);
        await purchasing.AttachToAnnouncementAsync(announcement.Id, unitId);

        await units.CancelForOrderAsync(orderId);

        await using var db = await factory.CreateDbContextAsync();
        var unit = await db.ProductionUnits.SingleAsync(u => u.Id == unitId);
        Assert.Equal(ProductionUnitState.Cancelled, unit.State);
        Assert.Null(unit.SupplierDeliveryId);
    }

    // Regression: an announcement whose only remaining attached unit is Cancelled (e.g. pre-fix
    // data, or a state EF could still theoretically reach) must not read as open/overdue -
    // Cancelled units can never arrive, so they can't be what's holding the announcement open.
    [Fact]
    public async Task CancelledUnit_DoesNotKeepAnnouncementOpenOrOverdue()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var sentPoId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0001", SupplierOrderState.Sent);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var pastDate = DateTime.UtcNow.Date.AddDays(-1);
        var announcement = await purchasing.CreateAnnouncementAsync(supplierId, "DN-0001", pastDate);
        // Seeded directly via EF (not through AttachToAnnouncementAsync, which would reject a
        // Cancelled unit) to simulate a unit left dangling on the announcement pre-fix.
        await SeedUnitAsync(factory, supplierOrderId: sentPoId, state: ProductionUnitState.Cancelled, supplierDeliveryId: announcement.Id);

        var reloaded = await purchasing.ListAnnouncementsAsync(supplierId, openOnly: false);
        var announcementWithUnits = reloaded.Single(a => a.Id == announcement.Id);
        Assert.False(PurchasingService.IsOverdue(announcementWithUnits));

        var openAnnouncements = await purchasing.ListAnnouncementsAsync(supplierId, openOnly: true);
        Assert.DoesNotContain(openAnnouncements, a => a.Id == announcement.Id);
    }

    [Fact]
    public async Task ListUnitsAsync_AnnouncementFilter_ReturnsOnlyAttachedUnits()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var sentPoId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-0001", SupplierOrderState.Sent);
        var (_, attachedUnitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoId);
        var (_, otherUnitId) = await SeedUnitAsync(factory, supplierOrderId: sentPoId);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var announcement = await purchasing.CreateAnnouncementAsync(supplierId, "DN-0001", null);
        await purchasing.AttachToAnnouncementAsync(announcement.Id, attachedUnitId);
        var units = new ProductionUnitService(factory, WarehouseUser);

        var filtered = await units.ListUnitsAsync(supplierDeliveryId: announcement.Id);

        var only = Assert.Single(filtered);
        Assert.Equal(attachedUnitId, only.Id);
        Assert.DoesNotContain(filtered, u => u.Id == otherUnitId);
    }
}
