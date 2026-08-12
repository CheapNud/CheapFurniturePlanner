using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using CheapHelpers.Services.DataExchange.Pdf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 5: the region-scoped pool filter, the Expected-blocks-Depart guard, and the per-unit
// Delivered/Failed confirmation UI on /trips/{Id}. Harness mirrors TripPagesTests (same
// TestDbContextFactory/NewFactoryAsync shape, same PartyService + TripLoadListPdf registrations
// TripPage now needs to resolve), reusing its SeedPlacedOrderWithUnitsAsync pattern extended with
// an optional delivery-region and promise date for these fixtures.
public class TripPlanningUiTests : TestContext
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

    // Seeds a Seller/Consumer/placed Order with one deliver-to-warehouse unit, optionally with a
    // delivery address pinned to regionId. Returns the spawned unit's id and code.
    private static async Task<(int OrderId, int UnitId, string UnitCode)> SeedOrderWithUnitAsync(
        IDbContextFactory<FurniturePlannerContext> factory, ProductionUnitService units, int? regionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop", Multiplier = 1m };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();

        int? addressId = null;
        if (regionId is int wantedRegionId)
        {
            var address = new Address { Street = "Street", Number = "1", PostalCode = "1000", City = "City", RegionId = wantedRegionId };
            db.Addresses.Add(address);
            await db.SaveChangesAsync();
            addressId = address.Id;
        }

        var order = new Order
        {
            OrderNumber = $"ORD-2026-{await db.Orders.CountAsync() + 1:D4}",
            SellerId = seller.Id,
            ConsumerId = consumer.Id,
            MarketCode = "BE",
            State = OrderState.Placed,
            DeliveryAddressId = addressId,
            Lines = [new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 1 }],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await units.SpawnForOrderAsync(order.Id);
        var unit = (await units.UnitsForOrderAsync(order.Id)).Single();
        return (order.Id, unit.Id, unit.UnitCode);
    }

    private static async Task<(int OrderId, List<int> UnitIds)> SeedPlacedOrderWithUnitsAsync(
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
        var unitIds = (await units.UnitsForOrderAsync(order.Id)).Select(u => u.Id).ToList();
        return (order.Id, unitIds);
    }

    private static async Task<int> SeedRegionAsync(IDbContextFactory<FurniturePlannerContext> factory, string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        var region = new Region { Code = code, Name = code };
        db.Regions.Add(region);
        await db.SaveChangesAsync();
        return region.Id;
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser who)
    {
        var pdfRoot = Path.Combine(Path.GetTempPath(), "dp1-trip-ui-pdf-tests", Guid.NewGuid().ToString("N"));
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(who);
        Services.AddSingleton(sp => new PinnedCatalogueProvider(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new ProductionUnitService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), who, sp.GetRequiredService<PinnedCatalogueProvider>()));
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), who));
        Services.AddSingleton(sp => new TripLoadListPdf(factory, new PdfExportService(new PdfTemplateService()), pdfRoot));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task Pool_DefaultsToTripRegion_ToggleShowsAll()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var northRegionId = await SeedRegionAsync(factory, "NORTH");
        var (northOrderId, _, northUnitCode) = await SeedOrderWithUnitAsync(factory, units, northRegionId);
        var (regionlessOrderId, _, regionlessUnitCode) = await SeedOrderWithUnitAsync(factory, units, regionId: null);
        await units.ArriveByCodeAsync(northUnitCode);
        await units.ArriveByCodeAsync(regionlessUnitCode);
        var trip = await units.CreateTripAsync();
        await units.UpdateTripAsync(trip.Id, departureDate: null, truckName: null, driverName: null, regionId: northRegionId);
        ConfigureServices(factory, dock);

        var cut = Render<TripPage>(p => p.Add(x => x.Id, trip.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(northUnitCode, cut.Markup);
            Assert.DoesNotContain(regionlessUnitCode, cut.Markup);
        });

        var allRegionsSwitch = cut.FindComponent<MudSwitch<bool>>();
        await cut.InvokeAsync(() => allRegionsSwitch.Instance.ValueChanged.InvokeAsync(true));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(northUnitCode, cut.Markup);
            Assert.Contains(regionlessUnitCode, cut.Markup);
        });

        // Sanity: the seeded orders are actually the ones behind these codes.
        Assert.NotEqual(northOrderId, regionlessOrderId);
    }

    [Fact]
    public async Task Depart_DisabledWithExpectedAboard_TooltipNamesThem()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var (_, unitId, unitCode) = await SeedOrderWithUnitAsync(factory, units, regionId: null);
        var trip = await units.CreateTripAsync();
        ConfigureServices(factory, dock);

        var cut = Render<TripPage>(p => p.Add(x => x.Id, trip.Id));

        // The unit is still Expected (never arrived) - AssignableUnitsAsync now offers Expected
        // units too, so it shows up in the pool and gets loaded straight from the UI, exercising the
        // Depart guard end-to-end instead of pre-assigning it through the service.
        cut.WaitForAssertion(() => Assert.Contains(unitCode, cut.Markup));
        var loadButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Load");
        await cut.InvokeAsync(() => loadButton.Click());

        cut.WaitForAssertion(() =>
        {
            var departButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Depart");
            Assert.True(departButton.HasAttribute("disabled"));
        });

        var tooltip = cut.FindComponent<MudTooltip>();
        Assert.Contains(unitCode, tooltip.Instance.Text);

        var reloadedTrip = await units.GetTripAsync(trip.Id);
        Assert.Contains(reloadedTrip!.Units, u => u.Id == unitId && u.State == ProductionUnitState.Expected);
    }

    [Fact]
    public async Task ConfirmFlow_DeliverAndFailWithReason_ServiceState()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var dock = new FakeCurrentUser("dock-1", Roles.Warehouse);
        var units = new ProductionUnitService(factory, dock, new PinnedCatalogueProvider(factory));
        var (orderId, unitIds) = await SeedPlacedOrderWithUnitsAsync(factory, units, quantity: 2);
        var trip = await units.CreateTripAsync();
        foreach (var unitId in unitIds)
        {
            await units.ArriveAsync(unitId);
            await units.AssignToTripAsync(trip.Id, unitId);
        }
        await units.DepartAsync(trip.Id);
        ConfigureServices(factory, dock);

        var cut = Render<TripPage>(p => p.Add(x => x.Id, trip.Id));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("button").Count(b => b.TextContent.Trim() == "Delivered")));

        var deliverButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Delivered");
        await cut.InvokeAsync(() => deliverButton.Click());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button"), b => b.TextContent.Trim() == "Delivered"));

        var failButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Failed");
        await cut.InvokeAsync(() => failButton.Click());

        cut.WaitForAssertion(() => Assert.Contains(cut.FindComponents<MudTextField<string>>(), f => f.Instance.Label == "Reason"));
        var reasonField = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Reason");
        await cut.InvokeAsync(() => reasonField.Instance.ValueChanged.InvokeAsync("Damaged in transit"));

        var confirmButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Confirm");
        await cut.InvokeAsync(() => confirmButton.Click());

        cut.WaitForAssertion(() => Assert.Contains("Completed", cut.Markup));

        var finalUnits = await units.UnitsForOrderAsync(orderId);
        Assert.Single(finalUnits, u => u.State == ProductionUnitState.Delivered);
        var failedUnit = finalUnits.Single(u => u.State == ProductionUnitState.Arrived);
        Assert.Null(failedUnit.TripId);
        Assert.Equal("Damaged in transit", failedUnit.ReviewNote);

        var completedTrip = await units.GetTripAsync(trip.Id);
        Assert.Equal(TripState.Completed, completedTrip!.State);
    }
}
