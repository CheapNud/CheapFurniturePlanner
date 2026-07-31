using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 2: sweep resolution (line SupplierId beats a conflicting model map), draft reuse and
// PO-{yyyy}- max-suffix numbering (delete-safe, unlike a count-based scheme), the Draft/Sent
// lifecycle guards, model-map CRUD and the supplier-delete guard extension. Harness mirrors
// PlanningQueriesTests: in-memory SQLite, migrated schema, FakeCurrentUser, direct-EF seeding.
public class PurchasingServiceTests
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

    // Seeds a Seller/Consumer/placed Order with one DeliverToWarehouse-configurable line and one
    // ProductionUnit for it - the minimal chain a sweep candidate needs.
    private static async Task<(int UnitId, int OrderLineId)> SeedUnitAsync(
        IDbContextFactory<FurniturePlannerContext> factory,
        string modelCode,
        int? lineSupplierId = null,
        bool deliverToWarehouse = true,
        ProductionUnitState state = ProductionUnitState.Expected,
        int? supplierOrderId = null)
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
            ModelCode = modelCode,
            SupplierId = lineSupplierId,
            DeliverToWarehouse = deliverToWarehouse,
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
            CreatedAt = DateTime.UtcNow,
        };
        db.ProductionUnits.Add(unit);
        await db.SaveChangesAsync();
        return (unit.Id, line.Id);
    }

    [Fact]
    public async Task Sweep_LineSupplierId_BeatsConflictingModelMap()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var mapSupplierId = await SeedSupplierAsync(factory, "MAPSUP");
        var lineSupplierId = await SeedSupplierAsync(factory, "LINESUP");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = mapSupplierId, ModelCode = "FJORD" });
            await db.SaveChangesAsync();
        }
        var (unitId, _) = await SeedUnitAsync(factory, "FJORD", lineSupplierId: lineSupplierId);
        var purchasing = new PurchasingService(factory, OfficeUser);

        var result = await purchasing.GenerateOrdersAsync();

        var orderId = Assert.Single(result.SupplierOrderIds);
        var order = await purchasing.GetOrderAsync(orderId);
        Assert.Equal(lineSupplierId, order!.SupplierId);
        Assert.Equal(unitId, Assert.Single(order.Units).Id);
    }

    [Fact]
    public async Task Sweep_CreatesOneDraftPerSupplier()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        var supplierB = await SeedSupplierAsync(factory, "SUPB");
        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierA);
        await SeedUnitAsync(factory, "MODELB", lineSupplierId: supplierB);
        var purchasing = new PurchasingService(factory, OfficeUser);

        var result = await purchasing.GenerateOrdersAsync();

        Assert.Equal(2, result.SupplierOrderIds.Count);
        var orders = await purchasing.ListOrdersAsync();
        Assert.Equal(2, orders.Count);
        Assert.Contains(orders, o => o.SupplierId == supplierA && o.Units.Count == 1);
        Assert.Contains(orders, o => o.SupplierId == supplierB && o.Units.Count == 1);
    }

    [Fact]
    public async Task Sweep_ReusesExistingDraft_OnResweep()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var first = await purchasing.GenerateOrdersAsync();
        Assert.Single(first.SupplierOrderIds);

        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var second = await purchasing.GenerateOrdersAsync();

        Assert.Single(second.SupplierOrderIds);
        Assert.Equal(first.SupplierOrderIds[0], second.SupplierOrderIds[0]);
        var orders = await purchasing.ListOrdersAsync();
        var order = Assert.Single(orders);
        Assert.Equal(2, order.Units.Count);
    }

    [Fact]
    public async Task Sweep_ExcludesUnresolved_AlreadyOrdered_NonExpected_AndDropship()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var (includedUnitId, _) = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        await SeedUnitAsync(factory, "UNMAPPED"); // unresolved: no line supplier, no map
        var existingPoId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-9000", SupplierOrderState.Sent);
        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId, supplierOrderId: existingPoId); // already ordered
        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId, state: ProductionUnitState.Arrived); // non-Expected
        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId, deliverToWarehouse: false); // dropship to consumer

        var purchasing = new PurchasingService(factory, OfficeUser);
        var result = await purchasing.GenerateOrdersAsync();

        var orderId = Assert.Single(result.SupplierOrderIds);
        var order = await purchasing.GetOrderAsync(orderId);
        Assert.Equal(includedUnitId, Assert.Single(order!.Units).Id);
        Assert.Equal(["UNMAPPED"], result.UnresolvedModelCodes);
    }

    private static async Task<int> SeedSupplierOrderAsync(IDbContextFactory<FurniturePlannerContext> factory, int supplierId, string poNumber, SupplierOrderState state = SupplierOrderState.Draft)
    {
        await using var db = await factory.CreateDbContextAsync();
        var order = new SupplierOrder { PoNumber = poNumber, SupplierId = supplierId, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow, State = state };
        db.SupplierOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    // Seeds a Seller/Consumer/placed Order with one StandaloneArticle line whose SupplierRef
    // matched no Supplier (SupplierId null) and its ProductionUnit. Standalone lines have
    // ModelCode null by construction (Services/OrderEntryService.cs AddStandaloneLineAsync) - the
    // regression this covers: such a unit must still surface via the unresolved list, not vanish.
    private static async Task<(int UnitId, string AssignedCode)> SeedUnresolvedStandaloneUnitAsync(
        IDbContextFactory<FurniturePlannerContext> factory, string assignedCode)
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
            Kind = OrderLineKind.StandaloneArticle,
            AssignedCode = assignedCode,
            Quantity = 1,
            DeliverToWarehouse = true,
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
            State = ProductionUnitState.Expected,
            CreatedAt = DateTime.UtcNow,
        };
        db.ProductionUnits.Add(unit);
        await db.SaveChangesAsync();
        return (unit.Id, assignedCode);
    }

    [Fact]
    public async Task Sweep_UnresolvedStandaloneArticle_SurfacesAssignedCode()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var (_, assignedCode) = await SeedUnresolvedStandaloneUnitAsync(factory, "ART-9001");
        var purchasing = new PurchasingService(factory, OfficeUser);

        var result = await purchasing.GenerateOrdersAsync();

        Assert.Empty(result.SupplierOrderIds);
        Assert.Equal([assignedCode], result.UnresolvedModelCodes);
        Assert.Equal([assignedCode], await purchasing.UnresolvedModelCodesAsync());
    }

    [Fact]
    public async Task Sweep_UnresolvedModelCodes_Distinct()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedUnitAsync(factory, "GHOST");
        await SeedUnitAsync(factory, "GHOST");
        var purchasing = new PurchasingService(factory, OfficeUser);

        var result = await purchasing.GenerateOrdersAsync();

        Assert.Empty(result.SupplierOrderIds);
        Assert.Equal(["GHOST"], result.UnresolvedModelCodes);
        Assert.Equal(["GHOST"], await purchasing.UnresolvedModelCodesAsync());
    }

    [Fact]
    public async Task Sweep_Numbering_SurvivesDeletedDraft_NoCollision()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        var supplierB = await SeedSupplierAsync(factory, "SUPB");
        var (unitAId, _) = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierA);
        await SeedUnitAsync(factory, "MODELB", lineSupplierId: supplierB);
        var purchasing = new PurchasingService(factory, OfficeUser);

        var first = await purchasing.GenerateOrdersAsync();
        Assert.Equal(2, first.SupplierOrderIds.Count);
        var afterFirst = await purchasing.ListOrdersAsync();
        var orderA = afterFirst.Single(o => o.SupplierId == supplierA);
        var orderB = afterFirst.Single(o => o.SupplierId == supplierB);

        await purchasing.ReleaseUnitAsync(unitAId);
        await purchasing.DeleteOrderAsync(orderA.Id);

        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierA);
        var second = await purchasing.GenerateOrdersAsync();
        var newOrderId = Assert.Single(second.SupplierOrderIds);
        var newOrder = await purchasing.GetOrderAsync(newOrderId);

        // A naive count-based scheme (1 live PO left after the delete -> count+1) would reissue
        // orderB's own suffix here. Max-suffix numbering must always clear the highest live one.
        var suffixB = int.Parse(orderB.PoNumber.Split('-')[^1]);
        var suffixNew = int.Parse(newOrder!.PoNumber.Split('-')[^1]);
        Assert.True(suffixNew > suffixB, $"expected {newOrder.PoNumber} suffix to exceed {orderB.PoNumber} suffix");
        Assert.Null(await purchasing.GetOrderAsync(orderA.Id));
    }

    [Fact]
    public async Task ReleaseUnit_OnlyOnDraft_SentThrows()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var (unitId, _) = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var sweep = await purchasing.GenerateOrdersAsync();
        var orderId = Assert.Single(sweep.SupplierOrderIds);

        await purchasing.ReleaseUnitAsync(unitId);
        var draftOrder = await purchasing.GetOrderAsync(orderId);
        Assert.Empty(draftOrder!.Units);

        var (unitId2, _) = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        await purchasing.GenerateOrdersAsync(); // reuses the same draft, assigns unit2
        await purchasing.SendAsync(orderId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.ReleaseUnitAsync(unitId2));
    }

    [Fact]
    public async Task DeleteOrder_OnlyEmptyDraft()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var (unitId, _) = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var sweep = await purchasing.GenerateOrdersAsync();
        var orderId = Assert.Single(sweep.SupplierOrderIds);

        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.DeleteOrderAsync(orderId));

        await purchasing.ReleaseUnitAsync(unitId);
        await purchasing.DeleteOrderAsync(orderId);
        Assert.Null(await purchasing.GetOrderAsync(orderId));

        var (unitId2, _) = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var sweep2 = await purchasing.GenerateOrdersAsync();
        var orderId2 = Assert.Single(sweep2.SupplierOrderIds);
        await purchasing.SendAsync(orderId2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.DeleteOrderAsync(orderId2));
    }

    [Fact]
    public async Task Send_RequiresAtLeastOneUnit_StampsSentAt()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var (unitId, _) = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var sweep = await purchasing.GenerateOrdersAsync();
        var orderId = Assert.Single(sweep.SupplierOrderIds);

        await purchasing.ReleaseUnitAsync(unitId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.SendAsync(orderId));

        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        await purchasing.GenerateOrdersAsync();
        await purchasing.SendAsync(orderId);

        var sent = await purchasing.GetOrderAsync(orderId);
        Assert.Equal(SupplierOrderState.Sent, sent!.State);
        Assert.NotNull(sent.SentAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.SendAsync(orderId));
    }

    [Fact]
    public async Task SetTheirReference_SentOrCompleted()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var (unitId, _) = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var purchasing = new PurchasingService(factory, OfficeUser);
        var sweep = await purchasing.GenerateOrdersAsync();
        var orderId = Assert.Single(sweep.SupplierOrderIds);

        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.SetTheirReferenceAsync(orderId, "REF-1"));

        await purchasing.SendAsync(orderId);
        await purchasing.SetTheirReferenceAsync(orderId, "  REF-1  ");
        var order = await purchasing.GetOrderAsync(orderId);
        Assert.Equal("REF-1", order!.TheirReference);

        // A fast-completing PO (its one unit arrives) must still accept the supplier's ref - the
        // class comment promises TheirReference regardless of how quickly the PO closes.
        var units = new ProductionUnitService(factory, new FakeCurrentUser("wh-1", Roles.Warehouse));
        await units.ArriveAsync(unitId);
        var completed = await purchasing.GetOrderAsync(orderId);
        Assert.Equal(SupplierOrderState.Completed, completed!.State);

        await purchasing.SetTheirReferenceAsync(orderId, "REF-2");
        var final = await purchasing.GetOrderAsync(orderId);
        Assert.Equal("REF-2", final!.TheirReference);
    }

    [Fact]
    public async Task CreateAnnouncement_BlankReferenceThrows_DuplicateReferenceThrowsFriendly()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var purchasing = new PurchasingService(factory, OfficeUser);

        await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.CreateAnnouncementAsync(supplierId, "   ", null));

        await purchasing.CreateAnnouncementAsync(supplierId, "DN-0001", null);
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.CreateAnnouncementAsync(supplierId, "DN-0001", null));
        Assert.Contains("DN-0001", duplicate.Message);
    }

    [Fact]
    public async Task SupplierModelMap_Crud_UniqueCodeThrows_RemoveWorks()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        var supplierB = await SeedSupplierAsync(factory, "SUPB");
        var party = new PartyService(factory, OfficeUser);

        await party.AddSupplierModelMapAsync(supplierA, "  FJORD  ");
        var maps = await party.SupplierModelMapsAsync(supplierA);
        var map = Assert.Single(maps);
        Assert.Equal("FJORD", map.ModelCode);

        await Assert.ThrowsAsync<InvalidOperationException>(() => party.AddSupplierModelMapAsync(supplierB, "FJORD"));

        await party.RemoveSupplierModelMapAsync(map.Id);
        Assert.Empty(await party.SupplierModelMapsAsync(supplierA));
    }

    [Fact]
    public async Task DeleteSupplier_BlockedByModelMapOrPurchaseOrder()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var party = new PartyService(factory, OfficeUser);
        var mappedSupplier = await party.AddSupplierAsync("MAPPED", "Mapped Supplier");
        await party.AddSupplierModelMapAsync(mappedSupplier.Id, "FJORD");
        await Assert.ThrowsAsync<InvalidOperationException>(() => party.DeleteSupplierAsync(mappedSupplier.Id));

        var poSupplierId = await SeedSupplierAsync(factory, "POSUP");
        await SeedUnitAsync(factory, "MODELPO", lineSupplierId: poSupplierId);
        var purchasing = new PurchasingService(factory, OfficeUser);
        await purchasing.GenerateOrdersAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => party.DeleteSupplierAsync(poSupplierId));
    }

    [Fact]
    public async Task Mutations_RejectMechanicAndWarehouse()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var (unitId, _) = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var officePurchasing = new PurchasingService(factory, OfficeUser);
        var sweep = await officePurchasing.GenerateOrdersAsync();
        // Draft PO holding unitId - an Office call would SUCCEED here (ReleaseUnitAsync needs a
        // Draft-owned unit, SendAsync needs a Draft with >=1 unit), so the throw below can only be
        // the role guard, not a domain check with the same exception type.
        var draftOrderId = Assert.Single(sweep.SupplierOrderIds);
        // Empty Draft - an Office call to DeleteOrderAsync would SUCCEED here.
        var emptyDraftOrderId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-8001");
        // Sent - an Office call to SetTheirReferenceAsync would SUCCEED here.
        var sentOrderId = await SeedSupplierOrderAsync(factory, supplierId, "PO-2026-8002", SupplierOrderState.Sent);

        foreach (var role in new[] { Roles.Mechanic, Roles.Warehouse })
        {
            var intruder = new FakeCurrentUser("intruder", role);
            var purchasing = new PurchasingService(factory, intruder);
            var party = new PartyService(factory, intruder);
            await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.GenerateOrdersAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.ReleaseUnitAsync(unitId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.DeleteOrderAsync(emptyDraftOrderId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.SendAsync(draftOrderId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => purchasing.SetTheirReferenceAsync(sentOrderId, "x"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => party.AddSupplierModelMapAsync(supplierId, "X"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => party.RemoveSupplierModelMapAsync(0));
        }
    }
}
