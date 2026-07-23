using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        var units = new ProductionUnitService(factory, dock);
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
        var units = new ProductionUnitService(factory, dock);
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
    public async Task ScanEnter_UnknownCode_ShowsReviewList()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock);
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
