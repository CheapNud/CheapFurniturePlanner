using System.Linq;
using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Components.Parties;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
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

// Task 6: the Suppliers tab's "Models" button opens SupplierModelsDialog, mapping a supplier to
// the catalogue model codes it produces (PartyService.SupplierModelMapsAsync/Add/Remove). The
// add-by-code field shows a soft warning (a MudTooltip-wrapped icon, same idiom ReceivingPage
// uses for ReviewNote) when the trimmed code matches no model in the current published catalogue
// (ICatalogueSource.GetCurrentAsync) - the add still proceeds regardless. Harness mirrors
// PartiesAddressUiTests (bUnit + in-memory SQLite, real PartyService, dialogs render under
// MudDialogProvider) with a FakeCatalogueSource standing in for the published catalogue.
public class SupplierMapUiTests : TestContext
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class FakeCatalogueSource(CatalogueSnapshot snapshot) : ICatalogueSource
    {
        public Task<CatalogueSnapshot> GetCurrentAsync() => Task.FromResult(snapshot);

        public void Invalidate() { }
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

    private static CatalogueSnapshot NewSnapshot() => new()
    {
        Version = "1",
        Models = [new FurnitureModel { Code = "MODELA", Name = "Model A" }],
    };

    private static async Task<int> SeedSupplierAsync(IDbContextFactory<FurniturePlannerContext> factory, string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = code, Name = code };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), new FakeCurrentUser("office-1", Roles.Office)));
        Services.AddSingleton<ICatalogueSource>(new FakeCatalogueSource(NewSnapshot()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var dialogProvider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        return dialogProvider;
    }

    // Tab order on the page: Sellers(0), Consumers(1), Suppliers(2), Regions(3) - mirrors
    // PartiesAddressUiTests.ActivateTab.
    private static void ActivateSuppliersTab(IRenderedComponent<PartiesPage> cut)
    {
        var tab = cut.FindAll(".mud-tab")[2];
        cut.InvokeAsync(() => tab.Click());
    }

    private static IRenderedComponent<SupplierModelsDialog> OpenModelsDialog(
        IRenderedComponent<PartiesPage> cut, IRenderedComponent<MudDialogProvider> dialogProvider, out Task pendingClick)
    {
        ActivateSuppliersTab(cut);
        cut.WaitForAssertion(() => Assert.NotNull(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Models")));
        var modelsButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Models");
        pendingClick = cut.InvokeAsync(() => modelsButton.Click());
        dialogProvider.WaitForState(() => dialogProvider.FindComponents<SupplierModelsDialog>().Count > 0);
        return dialogProvider.FindComponent<SupplierModelsDialog>();
    }

    [Fact]
    public async Task ModelsDialog_AddUnknownCode_ShowsWarning_AndServiceReflectsAdd()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var dialogProvider = ConfigureServices(factory);

        var cut = Render<PartiesPage>();
        var dialog = OpenModelsDialog(cut, dialogProvider, out var pendingClick);

        // No code typed yet - no warning.
        Assert.Empty(dialog.FindComponents<MudTooltip>());

        var codeField = dialog.FindComponent<MudTextField<string>>();
        await dialog.InvokeAsync(() => codeField.Instance.ValueChanged.InvokeAsync("GHOST"));

        // GHOST matches no model in the fake published catalogue (only MODELA) - warning renders.
        dialog.WaitForAssertion(() => Assert.Single(dialog.FindComponents<MudTooltip>()));

        var addButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await dialog.InvokeAsync(() => addButton.Click());

        dialog.WaitForAssertion(() => Assert.Contains("GHOST", dialog.Markup));
        var parties = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var maps = await parties.SupplierModelMapsAsync(supplierId);
        Assert.Contains(maps, m => m.ModelCode == "GHOST");

        var closeButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Close");
        await dialog.InvokeAsync(() => closeButton.Click());
        await pendingClick;
    }

    [Fact]
    public async Task ModelsDialog_Remove_ClearsMapping()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var parties = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        await parties.AddSupplierModelMapAsync(supplierId, "MODELA");
        var dialogProvider = ConfigureServices(factory);

        var cut = Render<PartiesPage>();
        var dialog = OpenModelsDialog(cut, dialogProvider, out var pendingClick);
        dialog.WaitForAssertion(() => Assert.Contains("MODELA", dialog.Markup));

        var removeButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Remove");
        await dialog.InvokeAsync(() => removeButton.Click());

        dialog.WaitForAssertion(() => Assert.DoesNotContain("MODELA", dialog.Markup));
        var maps = await parties.SupplierModelMapsAsync(supplierId);
        Assert.Empty(maps);

        var closeButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Close");
        await dialog.InvokeAsync(() => closeButton.Click());
        await pendingClick;
    }
}
