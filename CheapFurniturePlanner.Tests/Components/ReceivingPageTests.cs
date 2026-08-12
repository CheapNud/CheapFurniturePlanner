using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 4: /receiving lists ProductionUnits via ProductionUnitService.ListUnitsAsync and drives the
// dock scan box through ArriveByCodeAsync. Harness mirrors ServiceListPageTests (bUnit + in-memory
// SQLite), seeding a placed order directly via EF (as ProductionUnitServiceTests does) then spawning
// units through the real service.
public class ReceivingPageTests : TestContext
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

    // Seeds a Seller/Consumer/placed Order with one deliver-to-warehouse line, then spawns its
    // units. Returns the order id and the seeded units' unit codes.
    private static async Task<(int OrderId, List<string> UnitCodes)> SeedPlacedOrderWithUnitsAsync(
        IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, int quantity = 1)
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
            Lines = [new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = quantity }],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await units.SpawnForOrderAsync(order.Id);
        var unitCodes = (await units.UnitsForOrderAsync(order.Id)).Select(u => u.UnitCode).ToList();
        return (order.Id, unitCodes);
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, ICurrentUser who)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(who);
        Services.AddSingleton(units);
        Services.AddSingleton(sp => new PurchasingService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), who));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task Render_ListsExpectedUnits()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var (_, unitCodes) = await SeedPlacedOrderWithUnitsAsync(factory, units);
        ConfigureServices(factory, units, dock);

        var cut = Render<ReceivingPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(unitCodes.Single(), cut.Markup);
            Assert.Contains("Expected", cut.Markup);
        });
    }

    [Fact]
    public async Task ScanEnter_ArrivesUnit()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var (orderId, unitCodes) = await SeedPlacedOrderWithUnitsAsync(factory, units);
        var seededCode = unitCodes.Single();
        ConfigureServices(factory, units, dock);

        var cut = Render<ReceivingPage>();
        cut.Find("input").Input(seededCode);
        cut.Find("input").KeyDown(Key.Enter);

        await cut.WaitForAssertionAsync(async () =>
        {
            var reloaded = await units.UnitsForOrderAsync(orderId);
            Assert.Equal(ProductionUnitState.Arrived, reloaded.Single().State);
        });
    }

    [Fact]
    public async Task Render_ReviewNote_ShowsTooltip()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var (_, unitCodes) = await SeedPlacedOrderWithUnitsAsync(factory, units);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var unit = await db.ProductionUnits.SingleAsync(u => u.UnitCode == unitCodes.Single());
            unit.ReviewNote = "Damaged in transit";
            await db.SaveChangesAsync();
        }
        ConfigureServices(factory, units, dock);

        var cut = Render<ReceivingPage>();

        // MudTooltip only mounts its popover content on hover, so it never shows up in cut.Markup;
        // TripPlanningUiTests hits the same wall and asserts through the component instance instead.
        cut.WaitForAssertion(() => Assert.Single(cut.FindComponents<MudTooltip>()));
        var tooltip = cut.FindComponent<MudTooltip>();
        Assert.Contains("Damaged in transit", tooltip.Instance.Text);
    }

    // Seeds a Deliver-to-Warehouse unit pinned to a supplier via the line's SupplierId (the
    // dropship pin PurchasingService.ResolveCandidatesAsync checks first), so GenerateOrdersAsync
    // sweeps it without needing a SupplierModelMap. Mirrors PurchasingUiTests.SeedUnitAsync.
    private static async Task<(int OrderId, string UnitCode)> SeedSweepableUnitAsync(
        IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, int supplierId, string orderNumber)
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop", Multiplier = 1m };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var order = new Order
        {
            OrderNumber = orderNumber,
            SellerId = seller.Id,
            ConsumerId = consumer.Id,
            MarketCode = "BE",
            State = OrderState.Placed,
            Lines = [new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, SupplierId = supplierId, DeliverToWarehouse = true, Quantity = 1 }],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await units.SpawnForOrderAsync(order.Id);
        var unitCode = (await units.UnitsForOrderAsync(order.Id)).Single().UnitCode;
        return (order.Id, unitCode);
    }

    [Fact]
    public async Task AnnouncementFilter_SelectsOnlyThatAnnouncementsUnits()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var purchasing = new PurchasingService(factory, office);

        await using var setupDb = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = "SUPA", Name = "Supplier A" };
        setupDb.Suppliers.Add(supplier);
        await setupDb.SaveChangesAsync();

        var (_, unitCodeOne) = await SeedSweepableUnitAsync(factory, units, supplier.Id, "ORD-2026-0001");
        var (_, unitCodeTwo) = await SeedSweepableUnitAsync(factory, units, supplier.Id, "ORD-2026-0002");

        var sweep = await purchasing.GenerateOrdersAsync();
        var poId = Assert.Single(sweep.SupplierOrderIds);
        await purchasing.SendAsync(poId);
        var order = await purchasing.GetOrderAsync(poId);
        var unitOneId = order!.Units.Single(u => u.UnitCode == unitCodeOne).Id;
        var unitTwoId = order.Units.Single(u => u.UnitCode == unitCodeTwo).Id;

        var announcement = await purchasing.CreateAnnouncementAsync(supplier.Id, "DN-0001", null);
        await purchasing.AttachToAnnouncementAsync(announcement.Id, unitOneId);
        var otherAnnouncement = await purchasing.CreateAnnouncementAsync(supplier.Id, "DN-0002", null);
        await purchasing.AttachToAnnouncementAsync(otherAnnouncement.Id, unitTwoId);

        ConfigureServices(factory, units, dock);

        var cut = Render<ReceivingPage>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(unitCodeOne, cut.Markup);
            Assert.Contains(unitCodeTwo, cut.Markup);
        });

        // MudSelect only mounts its item popover once opened, so the "Supplier A — DN-0001"
        // option label never appears in cut.Markup - drive the filter through the component
        // instance instead (mirrors PurchasingUiTests' unit/announcement selects).
        var announcementSelect = cut.FindComponent<MudSelect<int?>>();
        await cut.InvokeAsync(() => announcementSelect.Instance.ValueChanged.InvokeAsync(announcement.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(unitCodeOne, cut.Markup);
            Assert.DoesNotContain(unitCodeTwo, cut.Markup);
        });

        // Clearing the filter (back to null) restores the full list.
        await cut.InvokeAsync(() => announcementSelect.Instance.ValueChanged.InvokeAsync(null));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(unitCodeOne, cut.Markup);
            Assert.Contains(unitCodeTwo, cut.Markup);
        });
    }

    // Seeds a Deliver-to-Warehouse unit whose model maps to the null-supplier "made here" marker -
    // the same three-state rule Task 5's ListUnitsAsync(inHouseOnly:) filter and FinishAsync share.
    private static async Task<(int OrderId, string UnitCode)> SeedInHouseUnitAsync(
        IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, string orderNumber)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = "INHOUSE-1" });
        var seller = new Seller { Name = "Shop", Multiplier = 1m };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var order = new Order
        {
            OrderNumber = orderNumber,
            SellerId = seller.Id,
            ConsumerId = consumer.Id,
            MarketCode = "BE",
            State = OrderState.Placed,
            Lines = [new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, ModelCode = "INHOUSE-1", ElementCode = "EL1", DeliverToWarehouse = true, Quantity = 1 }],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await units.SpawnForOrderAsync(order.Id);
        var unitCode = (await units.UnitsForOrderAsync(order.Id)).Single().UnitCode;
        return (order.Id, unitCode);
    }

    [Fact]
    public async Task Render_ExcludesInHouseUnits_FromPool()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var (_, externalUnitCodes) = await SeedPlacedOrderWithUnitsAsync(factory, units);
        var (_, inHouseUnitCode) = await SeedInHouseUnitAsync(factory, units, "ORD-2026-0002");
        ConfigureServices(factory, units, dock);

        var cut = Render<ReceivingPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(externalUnitCodes.Single(), cut.Markup);
            Assert.DoesNotContain(inHouseUnitCode, cut.Markup);
        });
    }

    [Fact]
    public async Task ScanEnter_UnknownCode_ShowsReviewList()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        ConfigureServices(factory, units, dock);

        var cut = Render<ReceivingPage>();
        cut.Find("input").Input("NOPE-1-1");
        cut.Find("input").KeyDown(Key.Enter);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Needs review", cut.Markup);
            Assert.Contains("NOPE-1-1", cut.Markup);
        });
    }
}
