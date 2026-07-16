using AngleSharp.Dom;
using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Components.Studio;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Exercises the Draft-only option-list authoring UI: StudioElementOptionsPage lists an element's
// options via a MudTable, opens ChoiceOptionDialog/FabricOptionDialog for add/edit through
// MudDialogProvider (as StudioElementsPageTests drives ElementDialog), reorders via row arrows, and
// deletes through MudMessageBox. FJORD-STUDIO/FS2 is the seeded Draft element (options
// DEPTH/MECH/HEAD/STITCH/FABRIC); FJORD/FJ2 is the seeded Active element used to prove the
// frozen-state UI. Master fabric groups are AQUA/TERRA/HIDE/HIDE-THICK.
public class StudioElementOptionsPageTests : TestContext
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
    // explicitly pinned to Active here - mirrors StudioElementsPageTests.SeedModelStatesAsync.
    private static async Task SeedModelStatesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Active, State = TradeItemState.Active });
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
        await db.SaveChangesAsync();
    }

    // Same rationale as StudioElementsPageTests.ConfigureServices: any dialog the page opens
    // (ChoiceOptionDialog, FabricOptionDialog, MudMessageBox) renders as a descendant of the
    // MudDialogProvider root, not of the page under test, so callers must query the returned handle.
    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new AuthoringCatalogueStore(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton<ICatalogueSource, DbCatalogueSource>();
        Services.AddSingleton(sp => new CataloguePublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<ICatalogueSource>()));
        Services.AddSingleton(sp => new ModelPublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<CataloguePublishService>(), sp.GetRequiredService<ICatalogueSource>(), sp.GetRequiredService<AuthoringCatalogueStore>()));
        Services.AddSingleton(sp => new ArticleAuthoringService(sp.GetRequiredService<AuthoringCatalogueStore>(), sp.GetRequiredService<ModelPublishService>()));
        Services.AddSingleton(sp => new OptionAuthoringService(sp.GetRequiredService<AuthoringCatalogueStore>(), sp.GetRequiredService<ModelPublishService>(), sp.GetRequiredService<ArticleAuthoringService>()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var dialogProvider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        return dialogProvider;
    }

    private static IElement FindRow(IRenderedComponent<StudioElementOptionsPage> cut, string defCode) =>
        cut.FindAll("tbody tr").Single(tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == defCode));

    private static IElement FindRowButton(IRenderedComponent<StudioElementOptionsPage> cut, string defCode, string text) =>
        FindRow(cut, defCode).QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    // The reorder arrows carry no text, only a Title="Move up"/"Move down" parameter, which MudIconButton
    // falls through to as a native lowercase title attribute - locate by that rather than button position.
    private static IElement FindRowButtonByTitle(IRenderedComponent<StudioElementOptionsPage> cut, string defCode, string title) =>
        FindRow(cut, defCode).QuerySelectorAll("button").Single(b => b.GetAttribute("title") == title);

    [Fact]
    public async Task Render_ListsElementOptions()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));

        Assert.Contains("DEPTH", cut.Markup);
        Assert.Contains("MECH", cut.Markup);
        Assert.Contains("HEAD", cut.Markup);
        Assert.Contains("STITCH", cut.Markup);
        Assert.Contains("FABRIC", cut.Markup);
        Assert.NotNull(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Add choice option"));
        Assert.NotNull(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Add fabric option"));
    }

    [Fact]
    public async Task AddChoiceOption_ThroughDialog_Appends()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var addButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add choice option");

        // AddChoiceAsync awaits dialogRef.Result, which only resolves once the dialog closes -
        // awaiting the click itself here would deadlock the test; fire it and drive the dialog instead.
        var pendingClick = cut.InvokeAsync(() => addButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<ChoiceOptionDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<ChoiceOptionDialog>();

        var defCodeField = dialog.FindComponent<MudTextField<string>>();
        await dialog.InvokeAsync(() => defCodeField.Instance.ValueChanged.InvokeAsync("ARMS"));

        var addValueButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add value");
        await dialog.InvokeAsync(() => addValueButton.Click());

        var valueCodeField = dialog.FindComponents<MudTextField<string>>().Last();
        await dialog.InvokeAsync(() => valueCodeField.Instance.ValueChanged.InvokeAsync("NONE"));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var store = new AuthoringCatalogueStore(factory);
        var model = await store.LoadModelAsync(Studio);
        var element = model!.Elements.Single(e => e.Code == StudioElement);
        var added = Assert.Single(element.Options, o => o.OptionDefinitionCode == "ARMS");
        var choice = Assert.IsType<ChoiceOption>(added);
        Assert.Equal(["NONE"], choice.Values.Select(v => v.OptionChoiceCode));
        cut.WaitForAssertion(() => Assert.Contains("ARMS", cut.Markup));
    }

    [Fact]
    public async Task AddFabricOption_ThroughDialog_Appends()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var addButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add fabric option");

        var pendingClick = cut.InvokeAsync(() => addButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<FabricOptionDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<FabricOptionDialog>();

        var defCodeField = dialog.FindComponent<MudTextField<string>>();
        await dialog.InvokeAsync(() => defCodeField.Instance.ValueChanged.InvokeAsync("PIPING"));

        var fabricSelect = dialog.FindComponent<MudSelect<string>>();
        await dialog.InvokeAsync(() => fabricSelect.Instance.SelectedValuesChanged.InvokeAsync(new HashSet<string> { "AQUA" }));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var store = new AuthoringCatalogueStore(factory);
        var model = await store.LoadModelAsync(Studio);
        var element = model!.Elements.Single(e => e.Code == StudioElement);
        var added = Assert.Single(element.Options, o => o.OptionDefinitionCode == "PIPING");
        var fabric = Assert.IsType<FabricOption>(added);
        Assert.Equal(["AQUA"], fabric.FabricGroupCodes);
        cut.WaitForAssertion(() => Assert.Contains("PIPING", cut.Markup));
    }

    [Fact]
    public async Task EditOption_ChangesFlag()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var editButton = FindRowButton(cut, "STITCH", "Edit");

        var pendingClick = cut.InvokeAsync(() => editButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<ChoiceOptionDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<ChoiceOptionDialog>();

        var requiredSwitch = dialog.FindComponent<MudSwitch<bool>>();
        await dialog.InvokeAsync(() => requiredSwitch.Instance.ValueChanged.InvokeAsync(false));

        var submitButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Save");
        await cut.InvokeAsync(() => submitButton.Click());
        await pendingClick;

        var store = new AuthoringCatalogueStore(factory);
        var model = await store.LoadModelAsync(Studio);
        var updated = model!.Elements.Single(e => e.Code == StudioElement).Options.Single(o => o.OptionDefinitionCode == "STITCH");
        Assert.False(updated.Required);
    }

    [Fact]
    public async Task DeleteOption_ThroughConfirm_RemovesIt()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var deleteButton = FindRowButton(cut, "STITCH", "Delete");

        var pendingClick = cut.InvokeAsync(() => deleteButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        Assert.Contains("STITCH", messageBox.Markup);
        var confirmButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Delete");
        await cut.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        var store = new AuthoringCatalogueStore(factory);
        var model = await store.LoadModelAsync(Studio);
        Assert.DoesNotContain(model!.Elements.Single(e => e.Code == StudioElement).Options, o => o.OptionDefinitionCode == "STITCH");
        cut.WaitForAssertion(() => Assert.DoesNotContain(
            cut.FindAll("tbody tr"),
            tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == "STITCH")));
    }

    // DEPTH is referenced by FM-DEEP-FS2's condition (WHEN DEPTH=DEEP) but has no visibility rules -
    // the confirm message must fold in the BOM-line impact count.
    [Fact]
    public async Task DeleteReferencedOption_ConfirmShowsImpact()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var deleteButton = FindRowButton(cut, "DEPTH", "Delete");

        var pendingClick = cut.InvokeAsync(() => deleteButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        Assert.Contains("BOM line condition(s)", messageBox.Markup);

        var cancelButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel");
        await cut.InvokeAsync(() => cancelButton.Click());
        await pendingClick;
    }

    // MECH is FS2's seeded visibility trigger for HEAD (WHEN MECH=REC) and also the seeded
    // substitution's trigger (WHEN MECH=REC, replace FM-STD with FM-FIRM) - deleting it must
    // surface both kinds of impact in the confirm message.
    [Fact]
    public async Task DeleteMechOption_ConfirmShowsVisibilityAndSubstitutionImpact()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var deleteButton = FindRowButton(cut, "MECH", "Delete");

        var pendingClick = cut.InvokeAsync(() => deleteButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        Assert.Contains("visibility rule(s)", messageBox.Markup);
        Assert.Contains("substitution(s)", messageBox.Markup);

        var cancelButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel");
        await cut.InvokeAsync(() => cancelButton.Click());
        await pendingClick;
    }

    [Fact]
    public async Task Reorder_MovesOptionDown()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var store = new AuthoringCatalogueStore(factory);
        var before = (await store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == StudioElement).Options.Select(o => o.OptionDefinitionCode).ToList();
        Assert.Equal(["DEPTH", "MECH", "HEAD", "STITCH", "FABRIC"], before);

        var down = FindRowButtonByTitle(cut, "DEPTH", "Move down");
        await cut.InvokeAsync(() => down.Click());

        var after = (await store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == StudioElement).Options.Select(o => o.OptionDefinitionCode).ToList();
        Assert.Equal(["MECH", "DEPTH", "HEAD", "STITCH", "FABRIC"], after);
        cut.WaitForAssertion(() => Assert.NotNull(FindRow(cut, "MECH")));
    }

    [Fact]
    public async Task FrozenModel_DisablesControls()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Active).Add(x => x.ElementCode, ActiveElement));

        Assert.Null(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Add choice option"));
        Assert.Null(cut.FindAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Add fabric option"));

        var firstRow = cut.FindAll("tbody tr").First();
        var buttons = firstRow.QuerySelectorAll("button").ToList();
        Assert.True(buttons.Count >= 4);
        foreach (var button in buttons)
        {
            Assert.True(button.HasAttribute("disabled"));
        }
    }

    [Fact]
    public async Task VisibilityButton_PresentPerRow_ShowsRuleCount()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));

        // HEAD carries the seeded MECH:REC rule; STITCH has none.
        Assert.NotNull(FindRow(cut, "HEAD").QuerySelectorAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Visibility (1)"));
        Assert.NotNull(FindRow(cut, "STITCH").QuerySelectorAll("button").SingleOrDefault(b => b.TextContent.Trim() == "Visibility (0)"));
    }

    [Fact]
    public async Task VisibilityButton_DisabledWhenFrozen()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Active).Add(x => x.ElementCode, ActiveElement));

        var visibilityButtons = cut.FindAll("tbody tr")
            .SelectMany(tr => tr.QuerySelectorAll("button"))
            .Where(b => b.TextContent.Trim().StartsWith("Visibility ("))
            .ToList();
        Assert.NotEmpty(visibilityButtons);
        foreach (var button in visibilityButtons)
        {
            Assert.True(button.HasAttribute("disabled"));
        }
    }

    [Fact]
    public async Task VisibilityDialog_ListsAndRemovesRule()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var visibilityButton = FindRowButton(cut, "HEAD", "Visibility (1)");

        var pendingClick = cut.InvokeAsync(() => visibilityButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<VisibilityRulesDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<VisibilityRulesDialog>();

        Assert.Contains("MECH", dialog.Markup);
        Assert.Contains("REC", dialog.Markup);

        var removeButton = dialog.FindAll("button").Single(b => b.GetAttribute("title") == "Remove rule");
        await dialog.InvokeAsync(() => removeButton.Click());

        var store = new AuthoringCatalogueStore(factory);
        var model = await store.LoadModelAsync(Studio);
        var head = model!.Elements.Single(e => e.Code == StudioElement).Options.Single(o => o.OptionDefinitionCode == "HEAD");
        Assert.Empty(head.VisibilityRules);

        var closeButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Close");
        await cut.InvokeAsync(() => closeButton.Click());
        await pendingClick;
    }

    [Fact]
    public async Task VisibilityDialog_AddRule_Persists()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await SeedModelStatesAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<StudioElementOptionsPage>(p => p.Add(x => x.ModelCode, Studio).Add(x => x.ElementCode, StudioElement));
        var visibilityButton = FindRowButton(cut, "STITCH", "Visibility (0)");

        var pendingClick = cut.InvokeAsync(() => visibilityButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<VisibilityRulesDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<VisibilityRulesDialog>();

        var triggerSelect = dialog.FindComponent<MudSelect<string>>();
        await dialog.InvokeAsync(() => triggerSelect.Instance.ValueChanged.InvokeAsync("DEPTH"));

        var choiceSelect = dialog.FindComponents<MudSelect<string>>()[1];
        await dialog.InvokeAsync(() => choiceSelect.Instance.ValueChanged.InvokeAsync("DEEP"));

        var addButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Add");
        await dialog.InvokeAsync(() => addButton.Click());

        var store = new AuthoringCatalogueStore(factory);
        var model = await store.LoadModelAsync(Studio);
        var stitch = model!.Elements.Single(e => e.Code == StudioElement).Options.Single(o => o.OptionDefinitionCode == "STITCH");
        Assert.Contains(stitch.VisibilityRules, r => r.TriggerOptionDefinitionCode == "DEPTH" && r.TriggerChoiceCode == "DEEP");

        var closeButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Close");
        await cut.InvokeAsync(() => closeButton.Click());
        await pendingClick;
    }
}
