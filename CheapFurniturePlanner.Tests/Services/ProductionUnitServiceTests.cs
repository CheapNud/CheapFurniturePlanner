using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Configurator;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
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
        var service = new ProductionUnitService(factory, OfficeUser);
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
        var service = new ProductionUnitService(factory, OfficeUser);
        var orderId = await SeedOrderAsync(factory, OrderState.Placed,
            new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 1, DeliverToWarehouse = true },
            new OrderLine { DisplayIndex = 1, Kind = OrderLineKind.StandaloneArticle, Quantity = 1, SupplierRef = "SUP-X", DeliverToWarehouse = false });

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
        var service = new ProductionUnitService(factory, OfficeUser);
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
        var service = new ProductionUnitService(factory, OfficeUser);
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
        await articles.AddStandaloneAsync(new Article { AssignedCode = "ART-DROP", Name = "Pouf", ManualPrice = 79m, SupplierRef = "SUP-X", State = TradeItemState.Active });
        await publish.RepublishAsync();

        var pinned = new PinnedCatalogueProvider(factory);
        var units = new ProductionUnitService(factory, OfficeUser);
        var orders = new OrderEntryService(factory, source, pinned, units);
        var parties = new PartyService(factory);
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
}
