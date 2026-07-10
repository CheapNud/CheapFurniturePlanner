using AngleSharp.Dom;
using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Components.Studio;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Exercises the Draft-only element-list authoring UI: StudioElementsPage lists a model's elements
// via a MudTable, opens ElementDialog for add/edit through MudDialogProvider (as StudioPageTests
// drives NewModelDialog/EditModelDialog), reorders via row arrows, and deletes through
// MudMessageBox. FJORD-STUDIO is the seeded Draft model (elements FS2/FS3/FSCH); FJORD is the
// seeded Active model (elements FJ2/FJ3/FJCH) used to prove the frozen-state UI.
public class StudioElementsPageTests : TestContext
{
    private const string Studio = "FJORD-STUDIO";
    private const string Active = "FJORD";

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

    private static async Task SeedAuthoringStoreAsync(IDbContextFactory<FurniturePlannerContext> factory) =>
        await new AuthoringCatalogueStore(factory).SeedFromAsync(SeedCatalogue.Load());

    // GetStateAsync defaults an unrecorded model to Draft, so the frozen-state assertions need FJORD
    // explicitly pinned to Active here - mirrors ElementAuthoringServiceTests.SeedModelStatesAsync.
    private static async Task SeedModelStatesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Active, State = TradeItemState.Active });
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
        await db.SaveChangesAsync();
    }

    private static ModelPublishService NewPublishService(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        var source = new DbCatalogueSource(factory);
        return new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
    }

    // Same rationale as StudioPageTests.ConfigureServices: any dialog StudioElementsPage opens
    // (ElementDialog, MudMessageBox) renders as a descendant of the MudDialogProvider root, not of
    // the page under test, so callers must query the returned handle rather than `cut`.
    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new AuthoringCatalogueStore(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton<ICatalogueSource, DbCatalogueSource>();
        Services.AddSingleton(sp => new CataloguePublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<ICatalogueSource>()));
        Services.AddSingleton(sp => new ModelPublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<CataloguePublishService>(), sp.GetRequiredService<ICatalogueSource>(), sp.GetRequiredService<AuthoringCatalogueStore>()));
        Services.AddSingleton<ModelAuthoringService>();
        Services.AddSingleton(sp => new ElementAuthoringService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<AuthoringCatalogueStore>(), sp.GetRequiredService<ModelPublishService>()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var dialogProvider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        return dialogProvider;
    }

    private static IElement FindRow(IRenderedComponent<StudioElementsPage> cut, string elementCode) =>
        cut.FindAll("tbody tr").Single(tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == elementCode));

    // Row action cell renders four buttons in a fixed order: up-arrow, down-arrow, Edit, Delete.
    // The two arrows are icon-only (no text), so text lookup only works for Edit/Delete.
    private static IElement FindRowButton(IRenderedComponent<StudioElementsPage> cut, string elementCode, string text) =>
        FindRow(cut, elementCode).QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    private static (IElement Up, IElement Down, IElement Edit, IElement Delete) RowButtons(IRenderedComponent<StudioElementsPage> cut, string elementCode)
    {
        var buttons = FindRow(cut, elementCode).QuerySelectorAll("button").ToList();
        Assert.Equal(4, buttons.Count);
        return (buttons[0], buttons[1], buttons[2], buttons[3]);
    }

    [Fact]
    public async Task Render_ListsSeedElements()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementsPage>(p => p.Add(x => x.ModelCode, Studio));

        Assert.Contains("FS2", cut.Markup);
        Assert.Contains("FS3", cut.Markup);
        Assert.Contains("FSCH", cut.Markup);
        Assert.NotNull(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Add element"));
    }

    [Fact]
    public async Task AddElement_ThroughDialog_AppendsElement()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementsPage>(p => p.Add(x => x.ModelCode, Studio));
        var addButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add element");

        // AddElementAsync awaits dialogRef.Result, which only resolves once the dialog closes -
        // awaiting the click itself here would deadlock the test; fire it and drive the dialog instead.
        var pendingClick = cut.InvokeAsync(() => addButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<ElementDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<ElementDialog>();

        var textFields = dialog.FindComponents<MudTextField<string>>();
        await dialog.InvokeAsync(() => textFields[0].Instance.ValueChanged.InvokeAsync("SEAT"));
        await dialog.InvokeAsync(() => textFields[1].Instance.ValueChanged.InvokeAsync("Seat"));

        var numericFields = dialog.FindComponents<MudNumericField<double>>();
        await dialog.InvokeAsync(() => numericFields[0].Instance.ValueChanged.InvokeAsync(80d));
        await dialog.InvokeAsync(() => numericFields[1].Instance.ValueChanged.InvokeAsync(90d));
        await dialog.InvokeAsync(() => numericFields[2].Instance.ValueChanged.InvokeAsync(85d));

        var transportField = dialog.FindComponent<MudNumericField<int>>();
        await dialog.InvokeAsync(() => transportField.Instance.ValueChanged.InvokeAsync(2));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var store = new AuthoringCatalogueStore(factory);
        var model = await store.LoadModelAsync(Studio);
        var added = Assert.Single(model!.Elements, e => e.Code == "SEAT");
        Assert.Equal("Seat", added.Name);
        Assert.Equal(80d, added.Width);
        Assert.Equal(90d, added.Depth);
        Assert.Equal(85d, added.Height);
        Assert.Equal(2, added.TransportUnits);
        cut.WaitForAssertion(() => Assert.Contains("SEAT", cut.Markup));
    }

    [Fact]
    public async Task EditElement_ChangesName()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementsPage>(p => p.Add(x => x.ModelCode, Studio));
        var editButton = FindRowButton(cut, "FS2", "Edit");

        var pendingClick = cut.InvokeAsync(() => editButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<ElementDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<ElementDialog>();

        var textFields = dialog.FindComponents<MudTextField<string>>();
        await dialog.InvokeAsync(() => textFields[1].Instance.ValueChanged.InvokeAsync("Renamed Seat"));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Save");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var store = new AuthoringCatalogueStore(factory);
        var model = await store.LoadModelAsync(Studio);
        var updated = model!.Elements.Single(e => e.Code == "FS2");
        Assert.Equal("Renamed Seat", updated.Name);
        cut.WaitForAssertion(() => Assert.Contains("Renamed Seat", cut.Markup));
    }

    [Fact]
    public async Task Reorder_MovesElementDown()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementsPage>(p => p.Add(x => x.ModelCode, Studio));
        var store = new AuthoringCatalogueStore(factory);
        var before = (await store.LoadModelAsync(Studio))!.Elements.Select(e => e.Code).ToList();
        Assert.Equal(["FS2", "FS3", "FSCH"], before);

        var (_, down, _, _) = RowButtons(cut, "FS2");
        await cut.InvokeAsync(() => down.Click());

        var after = (await store.LoadModelAsync(Studio))!.Elements.Select(e => e.Code).ToList();
        Assert.Equal(["FS3", "FS2", "FSCH"], after);
        cut.WaitForAssertion(() => Assert.NotNull(FindRow(cut, "FS3")));
    }

    [Fact]
    public async Task DeleteElement_ThroughConfirm_RemovesIt()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementsPage>(p => p.Add(x => x.ModelCode, Studio));
        var deleteButton = FindRowButton(cut, "FS3", "Delete");

        var pendingClick = cut.InvokeAsync(() => deleteButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        Assert.Contains("FS3", messageBox.Markup);
        var confirmButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Delete");
        await cut.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        var store = new AuthoringCatalogueStore(factory);
        var model = await store.LoadModelAsync(Studio);
        Assert.DoesNotContain(model!.Elements, e => e.Code == "FS3");
        cut.WaitForAssertion(() => Assert.DoesNotContain(
            cut.FindAll("tbody tr"),
            tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == "FS3")));
    }

    [Fact]
    public async Task FrozenModel_DisablesMutatingControls()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementsPage>(p => p.Add(x => x.ModelCode, Active));

        Assert.Contains("FJ2", cut.Markup);
        Assert.Null(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Add element"));

        var (up, down, edit, delete) = RowButtons(cut, "FJ2");
        Assert.True(up.HasAttribute("disabled"));
        Assert.True(down.HasAttribute("disabled"));
        Assert.True(edit.HasAttribute("disabled"));
        Assert.True(delete.HasAttribute("disabled"));
    }
}
