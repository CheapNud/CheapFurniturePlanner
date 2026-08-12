using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Configurator;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 2: ProductionUnitService spawns one ProductionUnit per quantity of each deliver-to-warehouse
// line on order placement (and via idempotent backfill for pre-existing Placed orders), cancels open
// units when an order is cancelled, and derives a phase from a unit list. Harness mirrors
// ServiceTicketServiceTests: in-memory SQLite, migrated schema, FakeCurrentUser.
public class ProductionUnitServiceTests
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

    // Seeds a Seller/Consumer/Order directly via EF (no catalogue needed) with the given lines, in
    // the given state. Returns the order id.
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

    [Fact]
    public async Task Spawn_OnePerQuantity_WithSequentialCodes()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));
        var orderId = await SeedOrderAsync(factory, OrderState.Placed,
            new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 3 });

        await service.SpawnForOrderAsync(orderId);

        var units = await service.UnitsForOrderAsync(orderId);
        Assert.Equal(3, units.Count);
        Assert.Equal(["ORD-2026-0001-1-1", "ORD-2026-0001-1-2", "ORD-2026-0001-1-3"], units.Select(u => u.UnitCode));
        Assert.All(units, u => Assert.Equal(ProductionUnitState.Expected, u.State));
        Assert.Equal([1, 2, 3], units.Select(u => u.SequenceNumber));
    }

    [Fact]
    public async Task Spawn_SkipsDirectDropshipLines_AndIsIdempotent()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));
        var orderId = await SeedOrderAsync(factory, OrderState.Placed,
            new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 1, DeliverToWarehouse = true },
            new OrderLine { DisplayIndex = 1, Kind = OrderLineKind.StandaloneArticle, Quantity = 1, DeliverToWarehouse = false });

        await service.SpawnForOrderAsync(orderId);
        await service.SpawnForOrderAsync(orderId);

        var units = await service.UnitsForOrderAsync(orderId);
        Assert.Single(units);
    }

    [Fact]
    public async Task Spawn_IgnoresDraftOrders_ButBackfillCoversAllPlaced()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));
        var draftOrderId = await SeedOrderAsync(factory, OrderState.Draft,
            new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 1 });
        var placedOrderOneId = await SeedOrderAsync(factory, OrderState.Placed,
            new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 1 });
        var placedOrderTwoId = await SeedOrderAsync(factory, OrderState.Placed,
            new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 1 });

        await service.BackfillAsync();

        Assert.Empty(await service.UnitsForOrderAsync(draftOrderId));
        Assert.Single(await service.UnitsForOrderAsync(placedOrderOneId));
        Assert.Single(await service.UnitsForOrderAsync(placedOrderTwoId));

        await service.BackfillAsync();

        Assert.Empty(await service.UnitsForOrderAsync(draftOrderId));
        Assert.Single(await service.UnitsForOrderAsync(placedOrderOneId));
        Assert.Single(await service.UnitsForOrderAsync(placedOrderTwoId));
    }

    [Fact]
    public async Task CancelForOrder_CancelsOpenUnits_LeavesDelivered_ReleasesTrip()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));
        var orderId = await SeedOrderAsync(factory, OrderState.Placed,
            new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 3 });
        await service.SpawnForOrderAsync(orderId);

        int expectedUnitId, arrivedUnitId, deliveredUnitId, tripId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var units = await db.ProductionUnits.Where(u => u.OrderId == orderId).OrderBy(u => u.SequenceNumber).ToListAsync();
            var trip = new Trip { TripCode = "TRIP-1", State = TripState.Planning };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            expectedUnitId = units[0].Id; // stays Expected

            units[1].State = ProductionUnitState.Arrived;
            units[1].TripId = trip.Id;
            units[1].LoadPosition = 1;
            arrivedUnitId = units[1].Id;

            units[2].State = ProductionUnitState.Delivered;
            deliveredUnitId = units[2].Id;

            tripId = trip.Id;
            await db.SaveChangesAsync();
        }

        await service.CancelForOrderAsync(orderId);

        await using var check = await factory.CreateDbContextAsync();
        var reloadedExpected = await check.ProductionUnits.SingleAsync(u => u.Id == expectedUnitId);
        var reloadedArrived = await check.ProductionUnits.SingleAsync(u => u.Id == arrivedUnitId);
        var reloadedDelivered = await check.ProductionUnits.SingleAsync(u => u.Id == deliveredUnitId);
        Assert.Equal(ProductionUnitState.Cancelled, reloadedExpected.State);
        Assert.Equal(ProductionUnitState.Cancelled, reloadedArrived.State);
        Assert.Null(reloadedArrived.TripId);
        Assert.Equal(ProductionUnitState.Delivered, reloadedDelivered.State);
    }

    [Fact]
    public void DerivePhase_AllBoundaries()
    {
        static ProductionUnit Unit(ProductionUnitState state) => new() { UnitCode = "X", State = state };

        Assert.Null(ProductionUnitService.DerivePhase([]));
        Assert.Equal(ProductionPhase.InProduction, ProductionUnitService.DerivePhase([Unit(ProductionUnitState.Expected)]));
        Assert.Equal(ProductionPhase.Ready, ProductionUnitService.DerivePhase([Unit(ProductionUnitState.Arrived)]));
        Assert.Equal(ProductionPhase.Ready, ProductionUnitService.DerivePhase([Unit(ProductionUnitState.Arrived), Unit(ProductionUnitState.Delivered)]));
        Assert.Equal(ProductionPhase.Delivered, ProductionUnitService.DerivePhase([Unit(ProductionUnitState.Delivered)]));
        Assert.Null(ProductionUnitService.DerivePhase([Unit(ProductionUnitState.Cancelled)]));
        Assert.Equal(ProductionPhase.Delivered, ProductionUnitService.DerivePhase([Unit(ProductionUnitState.Cancelled), Unit(ProductionUnitState.Delivered)]));
    }

    // -- hook tests: real service graph mirroring OrderEntryServiceTests --

    private sealed record Harness(OrderEntryService Orders, ProductionUnitService Units, Seller Seller, Consumer Consumer, Article Article);

    private static async Task<Harness> NewOrderHarnessAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var model in SeedCatalogue.Load().Models)
            {
                db.ModelStates.Add(new ModelStateRecord { ModelCode = model.Code, State = TradeItemState.Active });
            }
            await db.SaveChangesAsync();
        }
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        var articles = new ArticleAuthoringService(store, publish);
        var parties = new PartyService(factory, OfficeUser);
        // SetDeliverToWarehouseAsync now gates on the resolved Supplier FK, not the legacy ref
        // string, so the dropship article needs a real Supplier "SUP-X" for AddStandaloneLineAsync
        // to resolve SupplierId against.
        await parties.AddSupplierAsync("SUP-X", "Sup X Wholesale");
        await articles.AddStandaloneAsync(new Article { AssignedCode = "ART-DROP", Name = "Pouf", ManualPrice = 79m, SupplierRef = "SUP-X", State = TradeItemState.Active });
        await publish.RepublishAsync();

        var pinned = new PinnedCatalogueProvider(factory);
        var units = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));
        var orders = new OrderEntryService(factory, source, pinned, units);
        var seller = await parties.AddSellerAsync("Northwind Reseller", 1.2m);
        var consumer = await parties.AddConsumerAsync("Jane Consumer", "jane@example.com");
        var article = (await store.LoadArticlesAsync()).Single(a => a.AssignedCode == "ART-DROP");
        return new Harness(orders, units, seller, consumer, article);
    }

    [Fact]
    public async Task PlaceAsync_SpawnsUnits()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");
        await harness.Orders.AddStandaloneLineAsync(order.Id, harness.Article.Id, 4);

        await harness.Orders.PlaceAsync(order.Id);

        var units = await harness.Units.UnitsForOrderAsync(order.Id);
        Assert.Equal(4, units.Count);
        Assert.All(units, u => Assert.Equal(ProductionUnitState.Expected, u.State));
    }

    [Fact]
    public async Task CancelAsync_CancelsUnits()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");
        await harness.Orders.AddStandaloneLineAsync(order.Id, harness.Article.Id, 2);
        await harness.Orders.PlaceAsync(order.Id);

        await harness.Orders.CancelAsync(order.Id);

        var units = await harness.Units.UnitsForOrderAsync(order.Id);
        Assert.Equal(2, units.Count);
        Assert.All(units, u => Assert.Equal(ProductionUnitState.Cancelled, u.State));
    }

    [Fact]
    public async Task SetDeliverToWarehouse_OnlyDraft_OnlyDropshipCapable()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");
        await harness.Orders.AddStandaloneLineAsync(order.Id, harness.Article.Id, 1);
        var dropshipLine = (await harness.Orders.GetOrderAsync(order.Id))!.Lines.Single();

        await harness.Orders.SetDeliverToWarehouseAsync(order.Id, dropshipLine.Id, false);

        var reloaded = (await harness.Orders.GetOrderAsync(order.Id))!.Lines.Single();
        Assert.False(reloaded.DeliverToWarehouse);

        var (_, selections, fabricColorCode) = Fj2Default(SeedCatalogue.Load());
        var configuredOrder = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUW");
        await harness.Orders.AddConfiguredLineAsync(configuredOrder.Id, "FJORD", "FJ2", selections, fabricColorCode, 1);
        var configuredLine = (await harness.Orders.GetOrderAsync(configuredOrder.Id))!.Lines.Single();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Orders.SetDeliverToWarehouseAsync(configuredOrder.Id, configuredLine.Id, false));

        await harness.Orders.PlaceAsync(order.Id);
        await Assert.ThrowsAsync<OrderPlacedException>(() =>
            harness.Orders.SetDeliverToWarehouseAsync(order.Id, dropshipLine.Id, true));
    }

    private static (Element Element, Dictionary<string, string> Selections, string? FabricColorCode) Fj2Default(CatalogueSnapshot snapshot)
    {
        var element = snapshot.Models.SelectMany(m => m.Elements).Single(e => e.Code == "FJ2");
        var selections = ConfigurationResolver.DefaultSelections(element);
        var fabricColorCode = ConfigurationResolver.DefaultFabricColorCode(element, snapshot);
        return (element, selections, fabricColorCode);
    }

    // -- Task 5: finish flow + backflush --

    private static readonly Dictionary<string, string> StdSelections = new() { ["DEPTH"] = "STD", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" };

    // Same embedded "Fjord" demo bundle MaterialNeedsServiceTests uses - stamps the given version +
    // a real ComputeContentHash and inserts the row directly.
    private static void SeedPublishedCatalogue(IDbContextFactory<FurniturePlannerContext> factory, string version)
    {
        var asm = typeof(CataloguePublishService).Assembly;
        using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json")
            ?? throw new InvalidOperationException("Embedded resource 'CheapFurniturePlanner.Seed.demo-catalogue.json' not found.");
        using var reader = new StreamReader(stream);
        var snapshot = CanonicalJson.Deserialize<CatalogueSnapshot>(reader.ReadToEnd())
            ?? throw new InvalidOperationException("Failed to deserialize embedded demo-catalogue.json.");
        snapshot.Version = version;
        snapshot.ContentHash = snapshot.ComputeContentHash();
        using var db = factory.CreateDbContext();
        db.PublishedCatalogues.Add(new PublishedCatalogue { Version = version, ContentHash = snapshot.ContentHash, BundleJson = CanonicalJson.Serialize(snapshot), IsCurrent = true });
        db.SaveChanges();
    }

    // Seeds a Placed order pinned to version "1" with one FJ2 configured line (StdSelections,
    // AQUA-BLUE fabric - same BOM MaterialNeedsServiceTests pins: frame FBX x1, foam FM-STD x2,
    // cotton COT-STD x3.0, fabric AQUA-BLUE x4.0, misc GLUE x4) and one Expected unit on it.
    // inHouse true adds the null-supplier map row that marks the model "made here".
    private static async Task<(int OrderId, int UnitId, string UnitCode)> SeedFj2UnitAsync(
        IDbContextFactory<FurniturePlannerContext> factory, bool inHouse, int? lineSupplierId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (inHouse) { db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = "FJORD" }); }
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
            PinnedCatalogueVersion = "1",
        };
        order.Lines.Add(new OrderLine
        {
            DisplayIndex = 0,
            Kind = OrderLineKind.ConfiguredElement,
            ModelCode = "FJORD",
            ElementCode = "FJ2",
            SelectionsJson = CanonicalJson.Serialize(StdSelections),
            FabricColorCode = "AQUA-BLUE",
            SupplierId = lineSupplierId,
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
            State = ProductionUnitState.Expected,
            CreatedAt = DateTime.UtcNow,
        };
        db.ProductionUnits.Add(unit);
        await db.SaveChangesAsync();
        return (order.Id, unit.Id, unit.UnitCode);
    }

    [Fact]
    public async Task Finish_MovesToArrivedAndBackflushes()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");
        var (_, unitId, _) = await SeedFj2UnitAsync(factory, inHouse: true);
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));

        await service.FinishAsync(unitId);

        await using var db = await factory.CreateDbContextAsync();
        var unit = await db.ProductionUnits.SingleAsync(u => u.Id == unitId);
        Assert.Equal(ProductionUnitState.Arrived, unit.State);
        Assert.NotNull(unit.ArrivedAt);

        var stocks = await db.MaterialStocks.ToDictionaryAsync(s => (s.Kind, s.Code, s.HardnessCode), s => s.Amount);
        Assert.Equal(-1m, stocks[(MaterialKind.Frame, "FBX", null)]);
        Assert.Equal(-2m, stocks[(MaterialKind.Foam, "FM-STD", null)]);
        Assert.Equal(-3.0m, stocks[(MaterialKind.Cotton, "COT-STD", null)]);
        Assert.Equal(-4.0m, stocks[(MaterialKind.Fabric, "AQUA-BLUE", null)]);
        Assert.Equal(-4m, stocks[(MaterialKind.Misc, "GLUE", null)]);
    }

    [Fact]
    public async Task Finish_RejectsExternalAndUnresolved()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");
        int supplierId;
        await using (var setupDb = await factory.CreateDbContextAsync())
        {
            var supplier = new Supplier { Code = "SUPX", Name = "Sup X" };
            setupDb.Suppliers.Add(supplier);
            await setupDb.SaveChangesAsync();
            supplierId = supplier.Id;
        }
        var (_, dropshipUnitId, _) = await SeedFj2UnitAsync(factory, inHouse: false, lineSupplierId: supplierId); // dropship-pinned line
        var (_, unresolvedUnitId, _) = await SeedFj2UnitAsync(factory, inHouse: false); // no map row at all
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));

        var dropshipEx = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinishAsync(dropshipUnitId));
        Assert.Contains("not marked in-house", dropshipEx.Message);
        var unresolvedEx = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinishAsync(unresolvedUnitId));
        Assert.Contains("not marked in-house", unresolvedEx.Message);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.MaterialStocks.ToListAsync());
    }

    [Fact]
    public async Task UndoArrive_ReIncrementsForInHouse()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");
        var (_, unitId, _) = await SeedFj2UnitAsync(factory, inHouse: true);
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));
        await service.FinishAsync(unitId);

        await service.UndoArriveAsync(unitId);

        await using var db = await factory.CreateDbContextAsync();
        var unit = await db.ProductionUnits.SingleAsync(u => u.Id == unitId);
        Assert.Equal(ProductionUnitState.Expected, unit.State);
        Assert.Null(unit.ArrivedAt);

        // Finish's -1 and undo's +1 net back to the pre-finish amount - no rows existed before, so
        // every row created along the way must be back at exactly zero.
        var stocks = await db.MaterialStocks.ToListAsync();
        Assert.Equal(5, stocks.Count);
        Assert.All(stocks, s => Assert.Equal(0m, s.Amount));
    }

    // Regression: undoing an arrival that never backflushed (the dock's external path) must never
    // start backflushing on the way back out - only FinishAsync's in-house consumption gets reversed.
    [Fact]
    public async Task UndoArrive_ExternalPathUnchanged()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var (_, unitId, _) = await SeedFj2UnitAsync(factory, inHouse: false); // unmapped - not in-house
        int poId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var supplier = new Supplier { Code = "SUPX", Name = "Sup X" };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            var po = new SupplierOrder { PoNumber = "PO-2026-0001", SupplierId = supplier.Id, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow, State = SupplierOrderState.Sent };
            db.SupplierOrders.Add(po);
            await db.SaveChangesAsync();
            poId = po.Id;
            (await db.ProductionUnits.SingleAsync(u => u.Id == unitId)).SupplierOrderId = poId;
            await db.SaveChangesAsync();
        }
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));

        await service.ArriveAsync(unitId);
        await using (var afterArrive = await factory.CreateDbContextAsync())
        {
            Assert.Equal(SupplierOrderState.Completed, (await afterArrive.SupplierOrders.SingleAsync(o => o.Id == poId)).State);
        }

        await service.UndoArriveAsync(unitId);

        await using var afterUndo = await factory.CreateDbContextAsync();
        Assert.Equal(SupplierOrderState.Sent, (await afterUndo.SupplierOrders.SingleAsync(o => o.Id == poId)).State);
        Assert.Equal(ProductionUnitState.Expected, (await afterUndo.ProductionUnits.SingleAsync(u => u.Id == unitId)).State);
        Assert.Empty(await afterUndo.MaterialStocks.ToListAsync());
    }

    // Global constraint: backflush fires ONLY on the in-house finish path, never on external
    // receiving - proven against an in-house-mapped unit (the worst case) arriving via the dock's
    // ArriveAsync rather than FinishAsync.
    [Fact]
    public async Task ExternalArrival_NeverBackflushes()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");
        var (_, unitId, _) = await SeedFj2UnitAsync(factory, inHouse: true);
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));

        await service.ArriveAsync(unitId);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(ProductionUnitState.Arrived, (await db.ProductionUnits.SingleAsync(u => u.Id == unitId)).State);
        Assert.Empty(await db.MaterialStocks.ToListAsync());
    }

    // Regression: a scanned in-house unit code must behave exactly like an unknown one - arriving it
    // through the dock would skip the backflush (ArriveByCodeAsync never applies it) yet still pull
    // the unit out of both the Expected pool and the finishing pool, and a later UndoArrive would
    // then mint stock that was never actually consumed.
    [Fact]
    public async Task ArriveByCode_ExcludesInHouseUnits_LeavesExpectedNoBackflush()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var (_, unitId, unitCode) = await SeedFj2UnitAsync(factory, inHouse: true);
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));

        var outcome = await service.ArriveByCodeAsync(unitCode);

        Assert.Equal(ScanOutcome.Unknown, outcome);
        await using var db = await factory.CreateDbContextAsync();
        var unit = await db.ProductionUnits.SingleAsync(u => u.Id == unitId);
        Assert.Equal(ProductionUnitState.Expected, unit.State);
        Assert.Empty(await db.MaterialStocks.ToListAsync());
    }

    // Regression: a unit can still carry a live SupplierOrderId after its model gets unmapped from
    // that supplier and remapped in-house (the reachable escape hatch) - FinishAsync must not
    // backflush materials never used and strand the still-Sent PO it's linked to.
    [Fact]
    public async Task Finish_RejectsUnitLinkedToSupplierOrder()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");
        var (_, unitId, _) = await SeedFj2UnitAsync(factory, inHouse: true);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var supplier = new Supplier { Code = "SUPX", Name = "Sup X" };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            var po = new SupplierOrder { PoNumber = "PO-2026-0001", SupplierId = supplier.Id, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow, State = SupplierOrderState.Sent };
            db.SupplierOrders.Add(po);
            await db.SaveChangesAsync();
            (await db.ProductionUnits.SingleAsync(u => u.Id == unitId)).SupplierOrderId = po.Id;
            await db.SaveChangesAsync();
        }
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinishAsync(unitId));
        Assert.Contains("ordered from a supplier", ex.Message);

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(ProductionUnitState.Expected, (await check.ProductionUnits.SingleAsync(u => u.Id == unitId)).State);
        Assert.Empty(await check.MaterialStocks.ToListAsync());
    }

    [Fact]
    public async Task ListUnits_InHouseFilterPartitions()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        int supplierId;
        await using (var setupDb = await factory.CreateDbContextAsync())
        {
            var supplier = new Supplier { Code = "SUPX", Name = "Sup X" };
            setupDb.Suppliers.Add(supplier);
            await setupDb.SaveChangesAsync();
            supplierId = supplier.Id;
        }
        var (_, inHouseUnitId, _) = await SeedFj2UnitAsync(factory, inHouse: true);
        // Both units share ModelCode "FJORD" (which the first call just marked in-house), so the
        // second one needs its own dropship pin to genuinely fall outside the in-house rule -
        // otherwise it would inherit the model-level map row same as MaterialNeedsService's sweep.
        var (_, externalUnitId, _) = await SeedFj2UnitAsync(factory, inHouse: false, lineSupplierId: supplierId);
        var service = new ProductionUnitService(factory, OfficeUser, new PinnedCatalogueProvider(factory));

        var inHouseOnly = await service.ListUnitsAsync(inHouseOnly: true);
        var externalOnly = await service.ListUnitsAsync(inHouseOnly: false);
        var unfiltered = await service.ListUnitsAsync();

        Assert.Equal([inHouseUnitId], inHouseOnly.Select(u => u.Id));
        Assert.Equal([externalUnitId], externalOnly.Select(u => u.Id));
        Assert.Equal(2, unfiltered.Count);
        Assert.Equal(unfiltered.Select(u => u.Id).OrderBy(id => id),
            inHouseOnly.Select(u => u.Id).Concat(externalOnly.Select(u => u.Id)).OrderBy(id => id));
    }
}
