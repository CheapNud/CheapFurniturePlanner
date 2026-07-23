using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 5: /trips lists trips and creates new ones via ProductionUnitService.CreateTripAsync;
// /trips/{Id} assigns arrived units, tracks load position and departs (delivering every loaded
// unit). Harness mirrors ReceivingPageTests (bUnit + in-memory SQLite, units spawned through the
// real service). The Depart confirm dialog is a MudMessageBox rendered under MudDialogProvider,
// same click-through-confirm-button pattern as StudioElementBomPageTests.DeleteLine_ThroughConfirm_RemovesIt.
public class TripPagesTests : TestContext
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

    // Seeds a Seller/Consumer/placed Order with one deliver-to-warehouse line of the given
    // quantity, then spawns its units - mirrors ReceivingPageTests.SeedPlacedOrderWithUnitsAsync.
    private static async Task<(int OrderId, List<string> UnitCodes)> SeedPlacedOrderWithUnitsAsync(
        IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, int quantity)
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

    // Returns the rendered MudDialogProvider handle - the Depart confirm MudMessageBox renders as
    // its descendant, not the page's, so callers must query it (as StudioElementBomPageTests does).
    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, ICurrentUser who)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(who);
        Services.AddSingleton(units);
        JSInterop.Mode = JSRuntimeMode.Loose;
        var dialogProvider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        return dialogProvider;
    }

    [Fact]
    public async Task TripDetail_AssignAndDepart_DeliversUnits()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock);
        var (orderId, unitCodes) = await SeedPlacedOrderWithUnitsAsync(factory, units, quantity: 2);
        foreach (var unitCode in unitCodes)
        {
            await units.ArriveByCodeAsync(unitCode);
        }
        var trip = await units.CreateTripAsync();
        var dialogProvider = ConfigureServices(factory, units, dock);

        var cut = Render<TripPage>(p => p.Add(x => x.Id, trip.Id));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("button").Count(b => b.TextContent.Trim() == "Load")));

        var firstLoad = cut.FindAll("button").First(b => b.TextContent.Trim() == "Load");
        await cut.InvokeAsync(() => firstLoad.Click());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button"), b => b.TextContent.Trim() == "Load"));

        var secondLoad = cut.FindAll("button").First(b => b.TextContent.Trim() == "Load");
        await cut.InvokeAsync(() => secondLoad.Click());
        cut.WaitForAssertion(() => Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Load"));

        var departButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Depart");
        var pendingClick = cut.InvokeAsync(() => departButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        var confirmButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Depart");
        await cut.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        cut.WaitForAssertion(() => Assert.Contains("Departed", cut.Markup));
        var reloaded = await units.UnitsForOrderAsync(orderId);
        Assert.All(reloaded, u => Assert.Equal(ProductionUnitState.Delivered, u.State));
    }

    [Fact]
    public async Task TripsList_ShowsTrips_AndCreates()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock);
        var seeded = await units.CreateTripAsync();
        ConfigureServices(factory, units, dock);

        var cut = Render<TripsPage>();

        cut.WaitForAssertion(() => Assert.Contains(seeded.TripCode, cut.Markup));

        var newTripButton = cut.Find("button");
        await cut.InvokeAsync(() => newTripButton.Click());

        var navigation = Services.GetRequiredService<NavigationManager>();
        var allTrips = await units.ListTripsAsync();
        var created = allTrips.Single(t => t.Id != seeded.Id);
        Assert.EndsWith($"/trips/{created.Id}", navigation.Uri);
    }
}
