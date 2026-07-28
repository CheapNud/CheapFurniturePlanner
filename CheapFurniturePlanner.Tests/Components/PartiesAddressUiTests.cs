using System.Linq;
using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Components.Parties;
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

// Task 6: /parties grew from two stacked sections into a four-tab page (Sellers / Consumers /
// Suppliers / Regions) plus address management - AddressEditor, SellerAddressDialog,
// ConsumerAddressBookDialog, SupplierDialog and RegionDialog, all wired through the Task-2
// PartyService address surface. Harness mirrors PartiesPageTests (bUnit + in-memory SQLite).
// MudTabs (KeepPanelsAlive=false, the default) only renders the *active* panel's content - tab
// headers are always present, but a panel's buttons/tables don't exist in the DOM until its tab
// is clicked. Tests click the relevant ".mud-tab" header before touching that panel.
public class PartiesAddressUiTests : TestContext
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (new TestDbContextFactory(options), conn);
    }

    // Any dialog PartiesPage opens (SupplierDialog, RegionDialog, ConsumerAddressBookDialog, ...)
    // renders as a descendant of the MudDialogProvider root, not of the page under test - mirrors
    // StudioElementsPageTests.ConfigureServices.
    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), new FakeCurrentUser("office-1", Roles.Office)));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var dialogProvider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        return dialogProvider;
    }

    // Tab order on the page: Sellers(0), Consumers(1), Suppliers(2), Regions(3).
    private static void ActivateTab(IRenderedComponent<PartiesPage> cut, int index)
    {
        var tab = cut.FindAll(".mud-tab")[index];
        cut.InvokeAsync(() => tab.Click());
    }

    [Fact]
    public async Task SuppliersTab_Crud_RoundTrips()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var dialogProvider = ConfigureServices(factory);

        var cut = Render<PartiesPage>();
        ActivateTab(cut, 2);
        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Add supplier")));

        var addButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add supplier");

        // AddSupplierAsync awaits dialogRef.Result, which only resolves once the dialog closes -
        // awaiting the click itself would deadlock the test; fire it and drive the dialog instead
        // (mirrors StudioElementsPageTests.AddElement_ThroughDialog_AppendsElement).
        var pendingClick = cut.InvokeAsync(() => addButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<SupplierDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<SupplierDialog>();

        var textFields = dialog.FindComponents<MudTextField<string>>();
        await dialog.InvokeAsync(() => textFields[0].Instance.ValueChanged.InvokeAsync("LAMPCO"));
        await dialog.InvokeAsync(() => textFields[1].Instance.ValueChanged.InvokeAsync("Lampco Supplies"));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var parties = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var suppliers = await parties.SuppliersAsync();
        Assert.Contains(suppliers, s => s.Code == "LAMPCO" && s.Name == "Lampco Supplies");
        cut.WaitForAssertion(() => Assert.Contains("LAMPCO", cut.Markup));
    }

    [Fact]
    public async Task RegionsTab_AddAndDeleteGuard()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var parties = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var dialogProvider = ConfigureServices(factory);

        var cut = Render<PartiesPage>();
        ActivateTab(cut, 3);
        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Add region")));

        var addButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add region");
        var pendingClick = cut.InvokeAsync(() => addButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<RegionDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<RegionDialog>();

        var textFields = dialog.FindComponents<MudTextField<string>>();
        await dialog.InvokeAsync(() => textFields[0].Instance.ValueChanged.InvokeAsync("NORTH"));
        await dialog.InvokeAsync(() => textFields[1].Instance.ValueChanged.InvokeAsync("Northern zone"));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var region = Assert.Single(await parties.RegionsAsync(), r => r.Code == "NORTH");

        // Reference the region from a seller's address so the delete guard fires.
        var seller = await parties.AddSellerAsync("Refs", 1m);
        await parties.SetSellerAddressAsync(seller.Id, new Address { Street = "Main", Number = "1", PostalCode = "1000", City = "Brussels", RegionId = region.Id });

        // Deleting through the service throws; the tab surfaces this as a Snackbar. Assert the
        // guard at the service boundary (mirrors PartiesPageTests.DeleteSellerWithOrders_ThrowsAtService).
        await Assert.ThrowsAsync<InvalidOperationException>(() => parties.DeleteRegionAsync(region.Id));
        Assert.Contains(await parties.RegionsAsync(), r => r.Id == region.Id);
    }

    [Fact]
    public async Task AddressBook_SetDefault_Flips()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var parties = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var consumer = await parties.AddConsumerAsync("Jansen", null);
        var first = await parties.AddDeliveryAddressAsync(consumer.Id, "Home", new Address { Street = "Kerkstraat", Number = "1", PostalCode = "9000", City = "Gent" });
        var second = await parties.AddDeliveryAddressAsync(consumer.Id, "Work", new Address { Street = "Marktplein", Number = "2", PostalCode = "9000", City = "Gent" });
        Assert.True(first.IsDefault);
        Assert.False(second.IsDefault);

        var dialogProvider = ConfigureServices(factory);
        var cut = Render<PartiesPage>();
        ActivateTab(cut, 1);
        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Addresses")));

        var addressesButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Addresses");
        var pendingClick = cut.InvokeAsync(() => addressesButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<ConsumerAddressBookDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<ConsumerAddressBookDialog>();
        dialog.WaitForState(() => dialog.FindAll("button").Any(b => b.TextContent.Trim() == "Set default"));

        var setDefaultButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Set default");
        await dialog.InvokeAsync(() => setDefaultButton.Click());

        var reloaded = await parties.DeliveryAddressesAsync(consumer.Id);
        Assert.True(reloaded.Single(d => d.Id == second.Id).IsDefault);
        Assert.False(reloaded.Single(d => d.Id == first.Id).IsDefault);

        var closeButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Close");
        await dialog.InvokeAsync(() => closeButton.Click());
        await pendingClick;
    }
}
