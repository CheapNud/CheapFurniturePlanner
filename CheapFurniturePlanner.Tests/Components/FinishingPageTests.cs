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

// Task 7: /finishing lists only the in-house Expected pool (ProductionUnitService.
// ListUnitsAsync(inHouseOnly: true, stateFilter: Expected)) and drives FinishAsync through the same
// scan-box idiom as ReceivingPage (Task 4). Harness mirrors ReceivingPageTests (bUnit + in-memory
// SQLite, real ProductionUnitService).
public class FinishingPageTests : TestContext
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

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, ICurrentUser who)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(who);
        Services.AddSingleton(units);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    // Seeds a Deliver-to-Warehouse unit whose model maps to the null-supplier "made here" marker -
    // same three-state rule ProductionUnitServiceTests/ReceivingPageTests use for the in-house pool.
    private static async Task<(int OrderId, string UnitCode)> SeedInHouseUnitAsync(
        IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, string orderNumber, string modelCode)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = modelCode });
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
            Lines = [new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, ModelCode = modelCode, ElementCode = "EL1", DeliverToWarehouse = true, Quantity = 1 }],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await units.SpawnForOrderAsync(order.Id);
        var unitCode = (await units.UnitsForOrderAsync(order.Id)).Single().UnitCode;
        return (order.Id, unitCode);
    }

    // Seeds an ordinary externally-sourced unit (no in-house map) - stays out of the finishing pool.
    private static async Task<(int OrderId, string UnitCode)> SeedExternalUnitAsync(
        IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, string orderNumber)
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
            Lines = [new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, ModelCode = "EXTERNAL-1", ElementCode = "EL2", DeliverToWarehouse = true, Quantity = 1 }],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await units.SpawnForOrderAsync(order.Id);
        var unitCode = (await units.UnitsForOrderAsync(order.Id)).Single().UnitCode;
        return (order.Id, unitCode);
    }

    [Fact]
    public async Task Render_ListsOnlyInHouseExpectedUnits()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var (_, inHouseCode) = await SeedInHouseUnitAsync(factory, units, "ORD-2026-0001", "MADE-HERE");
        var (_, externalCode) = await SeedExternalUnitAsync(factory, units, "ORD-2026-0002");
        ConfigureServices(factory, units, dock);

        var cut = Render<FinishingPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(inHouseCode, cut.Markup);
            Assert.DoesNotContain(externalCode, cut.Markup);
            Assert.Contains("MADE-HERE/EL1", cut.Markup);
        });
    }

    [Fact]
    public async Task Render_NoInHouseUnits_ShowsEmptyState()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        ConfigureServices(factory, units, dock);

        var cut = Render<FinishingPage>();

        cut.WaitForAssertion(() => Assert.Contains("Nothing to finish", cut.Markup));
    }

    [Fact]
    public async Task ScanEnter_FinishesUnit_CallsFinishAsyncAndReloads()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var (orderId, unitCode) = await SeedInHouseUnitAsync(factory, units, "ORD-2026-0001", "MADE-HERE");
        ConfigureServices(factory, units, dock);

        var cut = Render<FinishingPage>();
        cut.WaitForAssertion(() => Assert.Contains(unitCode, cut.Markup));
        cut.Find("input").Input(unitCode);
        cut.Find("input").KeyDown(Key.Enter);

        await cut.WaitForAssertionAsync(async () =>
        {
            var reloaded = await units.UnitsForOrderAsync(orderId);
            Assert.Equal(ProductionUnitState.Arrived, reloaded.Single().State);
            Assert.DoesNotContain(unitCode, cut.Markup);
        });
    }

    [Fact]
    public async Task ScanEnter_ExternalCode_ShowsFriendlyErrorSnackbar()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var (_, externalCode) = await SeedExternalUnitAsync(factory, units, "ORD-2026-0001");
        ConfigureServices(factory, units, dock);
        var snackbarHost = Render<MudSnackbarProvider>();

        var cut = Render<FinishingPage>();
        cut.Find("input").Input(externalCode);
        cut.Find("input").KeyDown(Key.Enter);

        snackbarHost.WaitForAssertion(() => Assert.Contains("is not in the finishing pool", snackbarHost.Markup));
    }
}
