using AngleSharp.Dom;
using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Components.Studio;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Bom;
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

// Exercises the Draft-only BOM-line authoring UI: StudioElementBomPage groups an element's BOM lines
// by BomSectionKind, each kind rendered as its own MudPaper card + MudTable, and opens BomLineDialog
// through MudDialogProvider (as StudioElementOptionsPageTests drives ChoiceOptionDialog/FabricOptionDialog).
// FJORD-STUDIO/FS2 is the seeded Draft element (Frame[FR-FS2], Foam[FM-BASE-FS2,FM-DEEP-FS2],
// Cotton[CT-FS2], CutSort[FB-FS2], Misc[MI-FS2,MI-ROUND-FS2], Labor[LB-CUT-FS2,LB-SEW-FS2]);
// FJORD/FJ2 is the seeded Active element used to prove the frozen-state UI. Master data: FrameBodies
// [FBX], Materials [FM-STD,FM-FIRM,FM-DEEP-PAD,COT-STD,GLUE,RESIN], Operations
// [OP-CUT,OP-SEW,OP-LEATHERWORK], FabricGroups [AQUA,TERRA,HIDE,HIDE-THICK].
public class StudioElementBomPageTests : TestContext
{
    private const string Studio = "FJORD-STUDIO";
    private const string Active = "FJORD";
    private const string StudioElement = "FS2";
    private const string ActiveElement = "FJ2";

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
    // explicitly pinned to Active here - mirrors StudioElementOptionsPageTests.SeedModelStatesAsync.
    private static async Task SeedModelStatesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Active, State = TradeItemState.Active });
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
        await db.SaveChangesAsync();
    }

    // Same rationale as StudioElementOptionsPageTests.ConfigureServices: any dialog the page opens
    // (BomLineDialog, MudMessageBox) renders as a descendant of the MudDialogProvider root, not of
    // the page under test, so callers must query the returned handle.
    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new AuthoringCatalogueStore(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton<ICatalogueSource, DbCatalogueSource>();
        Services.AddSingleton(sp => new CataloguePublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<ICatalogueSource>()));
        Services.AddSingleton(sp => new ModelPublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<CataloguePublishService>(), sp.GetRequiredService<ICatalogueSource>(), sp.GetRequiredService<AuthoringCatalogueStore>()));
        Services.AddSingleton(sp => new BomAuthoringService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<AuthoringCatalogueStore>(), sp.GetRequiredService<ModelPublishService>()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var dialogProvider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        return dialogProvider;
    }

    // Lines are unique by LineKey across the whole element's BOM, so a single lookup across every
    // kind's table (rather than one scoped to a specific card) is unambiguous.
    private static IElement FindRow(IRenderedComponent<StudioElementBomPage> cut, string lineKey) =>
        cut.FindAll("tbody tr").Single(tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == lineKey));

    private static IElement FindRowButton(IRenderedComponent<StudioElementBomPage> cut, string lineKey, string text) =>
        FindRow(cut, lineKey).QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    private static IElement FindRowButtonByTitle(IRenderedComponent<StudioElementBomPage> cut, string lineKey, string title) =>
        FindRow(cut, lineKey).QuerySelectorAll("button").Single(b => b.GetAttribute("title") == title);

    private static async Task<BomDocument> BomAsync(IDbContextFactory<FurniturePlannerContext> factory) =>
        (await new AuthoringCatalogueStore(factory).LoadModelAsync(Studio))!.Elements.Single(e => e.Code == StudioElement).Bom;

    [Fact]
    public async Task Render_ListsSeedBomLines()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementBomPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));

        Assert.Contains("FR-FS2", cut.Markup);
        Assert.Contains("LB-CUT-FS2", cut.Markup);
        Assert.Contains("MI-FS2", cut.Markup);
        foreach (var kind in Enum.GetValues<BomSectionKind>())
        {
            Assert.NotNull(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == $"Add {kind} line"));
        }
    }

    [Fact]
    public async Task AddLaborLine_ThroughDialog_Appends()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementBomPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var addButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add Labor line");

        // AddAsync awaits dialogRef.Result, which only resolves once the dialog closes - awaiting the
        // click itself here would deadlock the test; fire it and drive the dialog instead.
        var pendingClick = cut.InvokeAsync(() => addButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<BomLineDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<BomLineDialog>();

        var lineKeyField = dialog.FindComponent<MudTextField<string>>();
        await dialog.InvokeAsync(() => lineKeyField.Instance.ValueChanged.InvokeAsync("LB-NEW"));

        var operationSelect = dialog.FindComponent<MudSelect<string>>();
        await dialog.InvokeAsync(() => operationSelect.Instance.ValueChanged.InvokeAsync("OP-SEW"));

        // Numeric fields render in document order: Quantity (top-level), then Units (Labor group).
        var numericFields = dialog.FindComponents<MudNumericField<decimal>>();
        await dialog.InvokeAsync(() => numericFields[1].Instance.ValueChanged.InvokeAsync(3m));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var bom = await BomAsync(factory);
        var laborSection = bom.Sections.Single(s => s.Kind == BomSectionKind.Labor);
        var added = Assert.IsType<LaborBomLine>(laborSection.Lines.Single(l => l.LineKey == "LB-NEW"));
        Assert.Equal("OP-SEW", added.OperationCode);
        Assert.Equal(3m, added.Units);
        cut.WaitForAssertion(() => Assert.Contains("LB-NEW", cut.Markup));
    }

    [Fact]
    public async Task AddCutSortLine_WithSecondaryGroup_Persists()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementBomPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var addButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add CutSort line");

        var pendingClick = cut.InvokeAsync(() => addButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<BomLineDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<BomLineDialog>();

        var lineKeyField = dialog.FindComponent<MudTextField<string>>();
        await dialog.InvokeAsync(() => lineKeyField.Instance.ValueChanged.InvokeAsync("CS-NEW"));

        var addGroupButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add group metrage");
        await dialog.InvokeAsync(() => addGroupButton.Click());

        // The nested secondary-group row adds exactly one fabric-group select once "Add group
        // metrage" is clicked - the CutSort case itself renders no top-level select.
        var groupSelect = dialog.FindComponent<MudSelect<string>>();
        await dialog.InvokeAsync(() => groupSelect.Instance.ValueChanged.InvokeAsync("AQUA"));

        // Numeric fields render in document order: Quantity, Metrage, CutUnits, then the new row's Metrage.
        var numericFields = dialog.FindComponents<MudNumericField<decimal>>();
        await dialog.InvokeAsync(() => numericFields[3].Instance.ValueChanged.InvokeAsync(1.5m));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var bom = await BomAsync(factory);
        var cutSortSection = bom.Sections.Single(s => s.Kind == BomSectionKind.CutSort);
        var added = Assert.IsType<CutSortBomLine>(cutSortSection.Lines.Single(l => l.LineKey == "CS-NEW"));
        Assert.Equal(1.5m, added.SecondaryGroupMetrages["AQUA"]);
    }

    // Duplicate secondary-group fabric codes would crash ToDictionary in Submit() with an
    // ArgumentException, tearing down the circuit (no ErrorBoundary wraps the app). The dialog
    // must reject the duplicate via Snackbar instead of building the line.
    [Fact]
    public async Task AddCutSortLine_WithDuplicateSecondaryGroups_IsRejected()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementBomPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var addButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add CutSort line");

        var pendingClick = cut.InvokeAsync(() => addButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<BomLineDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<BomLineDialog>();

        var lineKeyField = dialog.FindComponent<MudTextField<string>>();
        await dialog.InvokeAsync(() => lineKeyField.Instance.ValueChanged.InvokeAsync("CS-DUP"));

        // Re-find the button after each click: adding a row re-renders the tree and the previous
        // element reference's event handler ID goes stale (bUnit throws UnknownEventHandlerIdException).
        await dialog.InvokeAsync(() => dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add group metrage").Click());
        await dialog.InvokeAsync(() => dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add group metrage").Click());

        var groupSelects = dialog.FindComponents<MudSelect<string>>();
        await dialog.InvokeAsync(() => groupSelects[0].Instance.ValueChanged.InvokeAsync("AQUA"));
        await dialog.InvokeAsync(() => groupSelects[1].Instance.ValueChanged.InvokeAsync("AQUA"));

        // Numeric fields render in document order: Quantity, Metrage, CutUnits, then each row's Metrage.
        var numericFields = dialog.FindComponents<MudNumericField<decimal>>();
        await dialog.InvokeAsync(() => numericFields[3].Instance.ValueChanged.InvokeAsync(1m));
        await dialog.InvokeAsync(() => numericFields[4].Instance.ValueChanged.InvokeAsync(2m));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await cut.InvokeAsync(() => submitButton.Click());

        // Submit() returns early on the duplicate-group guard, so the dialog never closes - the
        // click's own await returns immediately, but the outer AddAsync stays pending until the
        // provider tears the dialog down, which never happens here. Assert the dialog is still up
        // instead of awaiting pendingClick (which would hang).
        Assert.Single(dialogProvider.FindComponents<BomLineDialog>());

        var bom = await BomAsync(factory);
        var cutSortSection = bom.Sections.Single(s => s.Kind == BomSectionKind.CutSort);
        Assert.DoesNotContain(cutSortSection.Lines, l => l.LineKey == "CS-DUP");
    }

    [Fact]
    public async Task EditLine_ChangesField()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementBomPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var editButton = FindRowButton(cut, "FR-FS2", "Edit");

        var pendingClick = cut.InvokeAsync(() => editButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<BomLineDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<BomLineDialog>();

        var coloredSwitch = dialog.FindComponent<MudSwitch<bool>>();
        await dialog.InvokeAsync(() => coloredSwitch.Instance.ValueChanged.InvokeAsync(false));   // seed FR-FS2 is Colored:true

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Save");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var bom = await BomAsync(factory);
        var updated = Assert.IsType<FrameBomLine>(bom.Sections.Single(s => s.Kind == BomSectionKind.Frame).Lines.Single(l => l.LineKey == "FR-FS2"));
        Assert.False(updated.Colored);
    }

    [Fact]
    public async Task DeleteLine_ThroughConfirm_RemovesIt()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementBomPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var deleteButton = FindRowButton(cut, "MI-FS2", "Delete");

        var pendingClick = cut.InvokeAsync(() => deleteButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        Assert.Contains("MI-FS2", messageBox.Markup);
        var confirmButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Delete");
        await cut.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        var bom = await BomAsync(factory);
        var miscSection = bom.Sections.Single(s => s.Kind == BomSectionKind.Misc);   // MI-ROUND-FS2 keeps the section alive
        Assert.DoesNotContain(miscSection.Lines, l => l.LineKey == "MI-FS2");
        cut.WaitForAssertion(() => Assert.DoesNotContain(
            cut.FindAll("tbody tr"),
            tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == "MI-FS2")));
    }

    [Fact]
    public async Task Reorder_MovesLaborLineDown()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementBomPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var before = (await BomAsync(factory)).Sections.Single(s => s.Kind == BomSectionKind.Labor).Lines.Select(l => l.LineKey).ToList();
        Assert.Equal(["LB-CUT-FS2", "LB-SEW-FS2"], before);

        var down = FindRowButtonByTitle(cut, "LB-CUT-FS2", "Move down");
        await cut.InvokeAsync(() => down.Click());

        var after = (await BomAsync(factory)).Sections.Single(s => s.Kind == BomSectionKind.Labor).Lines.Select(l => l.LineKey).ToList();
        Assert.Equal(["LB-SEW-FS2", "LB-CUT-FS2"], after);
        cut.WaitForAssertion(() => Assert.NotNull(FindRow(cut, "LB-SEW-FS2")));
    }

    [Fact]
    public async Task FrozenModel_DisablesControls()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementBomPage>(p => p.Add(x => x.ModelCode, Active).Add(x => x.ElementCode, ActiveElement));

        var addButtons = cut.FindAll("button").Where(b => b.TextContent.Trim().StartsWith("Add ", StringComparison.Ordinal) && b.TextContent.Trim().EndsWith(" line", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(addButtons);
        foreach (var button in addButtons)
        {
            Assert.True(button.HasAttribute("disabled"));
        }

        var rowButtons = cut.FindAll("tbody tr").SelectMany(tr => tr.QuerySelectorAll("button")).ToList();
        Assert.NotEmpty(rowButtons);
        foreach (var button in rowButtons)
        {
            Assert.True(button.HasAttribute("disabled"));
        }
    }
}
