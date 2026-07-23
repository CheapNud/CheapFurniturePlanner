using AngleSharp.Dom;
using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Components.Studio;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Exercises the studio as the model MANAGER: it lists the authoring model set (the embedded seed
// catalogue) with each model's release state in a free-flow state picker, and offers create
// (blank/duplicate), rename and guarded delete. Runs against real ModelPublishService/
// ModelAuthoringService instances over in-memory SQLite, mirroring FurnitureConfigPanelTests/
// DbCatalogueSourceTests.
public class StudioPageTests : TestContext
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

    // RepublishAsync/GetAuthoringModelsAsync now read the authoring store rather than the embedded
    // seed directly, so the store must be seeded from that same embedded seed before either
    // NewPublishService or ConfigureServices below construct a ModelPublishService against it. Seeded
    // once per test, up front, so the two helpers below can freely share the same underlying DB
    // without seeding it twice.
    private static async Task SeedAuthoringStoreAsync(IDbContextFactory<FurniturePlannerContext> factory) =>
        await new AuthoringCatalogueStore(factory).SeedFromAsync(SeedCatalogue.Load());

    // The state machine now republishes the Active-only snapshot on every transition, so a service
    // built for out-of-band setup needs a real CataloguePublishService + ICatalogueSource.
    private static ModelPublishService NewPublishService(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        var source = new DbCatalogueSource(factory);
        return new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
    }

    // bUnit renders each Render<T>() call as its own root in the render tree, so any dialog
    // that DialogService.ShowAsync opens (MudMessageBox, NewModelDialog, EditModelDialog) shows up as
    // a descendant of the MudDialogProvider root, NOT of the page under test - callers must query the
    // returned handle, not `cut`.
    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new AuthoringCatalogueStore(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton<ICatalogueSource, DbCatalogueSource>();
        Services.AddSingleton(sp => new CataloguePublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<ICatalogueSource>()));
        Services.AddSingleton(sp => new ModelPublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<CataloguePublishService>(), sp.GetRequiredService<ICatalogueSource>(), sp.GetRequiredService<AuthoringCatalogueStore>()));
        Services.AddSingleton(sp => new ArticleAuthoringService(sp.GetRequiredService<AuthoringCatalogueStore>(), sp.GetRequiredService<ModelPublishService>()));
        Services.AddSingleton<ModelAuthoringService>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        // MudMessageBox confirmations and the New/Edit model dialogs render via MudDialogProvider;
        // MudSelect-style overlays elsewhere in the app rely on MudPopoverProvider - both need to be
        // present.
        var dialogProvider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        return dialogProvider;
    }

    // The table lists more than one authoring model, so buttons/pickers must be scoped to a model's
    // own row (identified by its Code cell) rather than picked globally.
    private static IElement FindRow(IRenderedComponent<StudioPage> cut, string modelCode) =>
        cut.FindAll("tbody tr").Single(tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == modelCode));

    private static IElement FindActionButton(IRenderedComponent<StudioPage> cut, string text, string modelCode = "FJORD") =>
        FindRow(cut, modelCode).QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    // The state MudSelect isn't a <button>, so it can't be located by text the way action buttons are.
    // Rows render in the same order as _models (Code-sorted), which matches the render order of the
    // MudSelect<TradeItemState> components 1:1 - so the row's index among <tr> elements is also its
    // index among the state selects.
    private static IRenderedComponent<MudSelect<TradeItemState>> FindStateSelect(IRenderedComponent<StudioPage> cut, string modelCode)
    {
        var rows = cut.FindAll("tbody tr").ToList();
        var rowIndex = rows.FindIndex(tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == modelCode));
        Assert.True(rowIndex >= 0, $"Row for '{modelCode}' not found.");
        return cut.FindComponents<MudSelect<TradeItemState>>()[rowIndex];
    }

    [Fact]
    public async Task Render_ListsSeedModel_WithDraftState_AndNameVariantsLink()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        ConfigureServices(factory);

        var cut = Render<StudioPage>();

        Assert.Contains("FJORD", cut.Markup);
        Assert.Contains("Fjord", cut.Markup);
        Assert.Contains(TradeItemState.Draft.ToString(), cut.Markup);

        Assert.Equal(TradeItemState.Draft, FindStateSelect(cut, "FJORD").Instance.Value);
        Assert.NotNull(FindActionButton(cut, "Name variants"));
    }

    [Fact]
    public async Task Render_ModelRow_HasElementsLink()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        ConfigureServices(factory);

        var cut = Render<StudioPage>();
        var elementsButton = FindActionButton(cut, "Elements");
        await cut.InvokeAsync(() => elementsButton.Click());

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/studio/FJORD/elements", navigation.Uri);
    }

    [Fact]
    public async Task Render_AfterOutOfBandRelease_ShowsActiveState()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        // Released out-of-band (as a second operator/tab would) before the page ever loads, so this
        // proves the initial load reflects persisted state rather than a page-local assumption.
        await NewPublishService(factory).SetStateAsync("FJORD", TradeItemState.Active);
        ConfigureServices(factory);

        var cut = Render<StudioPage>();

        Assert.Contains(TradeItemState.Active.ToString(), cut.Markup);
        Assert.Equal(TradeItemState.Active, FindStateSelect(cut, "FJORD").Instance.Value);
    }

    [Fact]
    public async Task ChangingStatePicker_TransitionsDraftToActive_AndPersists()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        ConfigureServices(factory);

        var cut = Render<StudioPage>();
        var stateSelect = FindStateSelect(cut, "FJORD");

        // Free-flow state change, no confirm dialog involved - driving the picker through the
        // component instance mirrors how StudioNamingPageTests drives MudTextField.ValueChanged.
        await cut.InvokeAsync(() => stateSelect.Instance.ValueChanged.InvokeAsync(TradeItemState.Active));

        Assert.Equal(TradeItemState.Active, await NewPublishService(factory).GetStateAsync("FJORD"));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(TradeItemState.Active.ToString(), cut.Markup);
            Assert.Equal(TradeItemState.Active, FindStateSelect(cut, "FJORD").Instance.Value);
        });
    }

    [Fact]
    public async Task ChangingStatePicker_TransitionsActiveToDiscontinued_AndPersists()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await NewPublishService(factory).SetStateAsync("FJORD", TradeItemState.Active);
        ConfigureServices(factory);

        var cut = Render<StudioPage>();
        var stateSelect = FindStateSelect(cut, "FJORD");

        await cut.InvokeAsync(() => stateSelect.Instance.ValueChanged.InvokeAsync(TradeItemState.Discontinued));

        Assert.Equal(TradeItemState.Discontinued, await NewPublishService(factory).GetStateAsync("FJORD"));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(TradeItemState.Discontinued.ToString(), cut.Markup);
            Assert.Equal(TradeItemState.Discontinued, FindStateSelect(cut, "FJORD").Instance.Value);
        });
    }

    [Fact]
    public async Task ChangingStatePicker_RevertsToDraft_WhenPublishFailsForNoElements()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        var authoringStore = new AuthoringCatalogueStore(factory);
        var authoringPublish = NewPublishService(factory);
        var authoring = new ModelAuthoringService(factory, authoringStore, authoringPublish, new ArticleAuthoringService(authoringStore, authoringPublish));
        await authoring.CreateBlankAsync("NOEL", "No Elements", null, null);
        ConfigureServices(factory);

        var cut = Render<StudioPage>();
        var stateSelect = FindStateSelect(cut, "NOEL");

        // NOEL has no elements, so CataloguePublishService rejects the republish, SetStateAsync
        // reverts the state row and rethrows, and ChangeStateAsync's catch reloads regardless - the
        // row should snap back to Draft rather than sticking on Active.
        await cut.InvokeAsync(() => stateSelect.Instance.ValueChanged.InvokeAsync(TradeItemState.Active));

        Assert.Equal(TradeItemState.Draft, await NewPublishService(factory).GetStateAsync("NOEL"));
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(TradeItemState.Draft, FindStateSelect(cut, "NOEL").Instance.Value);
        });
    }

    [Fact]
    public async Task NewModel_Blank_CreatesModel()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = Render<StudioPage>();
        var newModelButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "New model");

        // OpenNewModelDialogAsync awaits dialogRef.Result, which only resolves once the dialog closes -
        // awaiting the click itself here would deadlock the test; fire it and drive the dialog instead.
        var pendingClick = cut.InvokeAsync(() => newModelButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<NewModelDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<NewModelDialog>();

        // Blank is the default mode: Code, Name, Collection text fields render in that order.
        var fields = dialog.FindComponents<MudTextField<string>>();
        await dialog.InvokeAsync(() => fields[0].Instance.ValueChanged.InvokeAsync("NEWM"));
        await dialog.InvokeAsync(() => fields[1].Instance.ValueChanged.InvokeAsync("New Model"));

        var createButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Create");
        await cut.InvokeAsync(() => createButton.Click());
        await pendingClick;

        var models = await NewPublishService(factory).GetAuthoringModelsAsync();
        var created = Assert.Single(models, m => m.Code == "NEWM");
        Assert.Equal("New Model", created.Name);
        cut.WaitForAssertion(() => Assert.Contains("NEWM", cut.Markup));
    }

    [Fact]
    public async Task NewModel_Blank_TrailingWhitespaceCode_RejectedAsDuplicate()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        var authoringStore = new AuthoringCatalogueStore(factory);
        var authoringPublish = NewPublishService(factory);
        var authoring = new ModelAuthoringService(factory, authoringStore, authoringPublish, new ArticleAuthoringService(authoringStore, authoringPublish));
        await authoring.CreateBlankAsync("NEWM", "Existing", null, null);
        var dialogProvider = ConfigureServices(factory);

        var cut = Render<StudioPage>();
        var newModelButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "New model");

        var pendingClick = cut.InvokeAsync(() => newModelButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<NewModelDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<NewModelDialog>();

        // Trailing whitespace must not bypass the duplicate-code guard: "NEWM " has to be treated as
        // "NEWM", which already exists.
        var fields = dialog.FindComponents<MudTextField<string>>();
        await dialog.InvokeAsync(() => fields[0].Instance.ValueChanged.InvokeAsync("NEWM "));
        await dialog.InvokeAsync(() => fields[1].Instance.ValueChanged.InvokeAsync("Another New Model"));

        var createButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Create");
        await cut.InvokeAsync(() => createButton.Click());
        await pendingClick;

        // The service call throws, the dialog's catch reports it via Snackbar and does NOT close, so
        // the dialog is still open and no duplicate model got created.
        Assert.True(dialogProvider.FindComponents<NewModelDialog>().Count > 0);
        var models = await NewPublishService(factory).GetAuthoringModelsAsync();
        Assert.Single(models, m => m.Code == "NEWM");
    }

    [Fact]
    public async Task NewModel_Duplicate_ClonesSourceModel()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        var dialogProvider = ConfigureServices(factory);
        var store = new AuthoringCatalogueStore(factory);
        var sourceModel = await store.LoadModelAsync("FJORD");
        Assert.NotNull(sourceModel);

        var cut = Render<StudioPage>();
        var newModelButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "New model");

        var pendingClick = cut.InvokeAsync(() => newModelButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<NewModelDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<NewModelDialog>();

        var modeRadioGroup = dialog.FindComponent<MudRadioGroup<NewModelDialog.NewModelMode>>();
        await dialog.InvokeAsync(() => modeRadioGroup.Instance.ValueChanged.InvokeAsync(NewModelDialog.NewModelMode.Duplicate));

        var sourceSelect = dialog.FindComponent<MudSelect<string>>();
        await dialog.InvokeAsync(() => sourceSelect.Instance.ValueChanged.InvokeAsync("FJORD"));

        // In Duplicate mode the Collection field is hidden: only Code, Name remain.
        var fields = dialog.FindComponents<MudTextField<string>>();
        await dialog.InvokeAsync(() => fields[0].Instance.ValueChanged.InvokeAsync("FJORD-COPY"));
        await dialog.InvokeAsync(() => fields[1].Instance.ValueChanged.InvokeAsync("Fjord Copy"));

        var createButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Create");
        await cut.InvokeAsync(() => createButton.Click());
        await pendingClick;

        var clone = await store.LoadModelAsync("FJORD-COPY");
        Assert.NotNull(clone);
        Assert.Equal("Fjord Copy", clone!.Name);
        Assert.Equal(sourceModel!.Elements.Count, clone.Elements.Count);
    }

    [Fact]
    public async Task EditModel_ChangesName()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = Render<StudioPage>();
        var editButton = FindActionButton(cut, "Edit");

        var pendingClick = cut.InvokeAsync(() => editButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<EditModelDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<EditModelDialog>();

        // Code (index 0) is Disabled/read-only; Name is index 1.
        var nameField = dialog.FindComponents<MudTextField<string>>()[1];
        await dialog.InvokeAsync(() => nameField.Instance.ValueChanged.InvokeAsync("Fjord Renamed"));

        var saveButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Save");
        await cut.InvokeAsync(() => saveButton.Click());
        await pendingClick;

        var store = new AuthoringCatalogueStore(factory);
        var renamed = await store.LoadModelAsync("FJORD");
        Assert.Equal("Fjord Renamed", renamed!.Name);
        cut.WaitForAssertion(() => Assert.Contains("Fjord Renamed", cut.Markup));
    }

    [Fact]
    public async Task DeleteButton_DisabledForActiveModel()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        await NewPublishService(factory).SetStateAsync("FJORD", TradeItemState.Active);
        ConfigureServices(factory);

        var cut = Render<StudioPage>();

        Assert.True(FindActionButton(cut, "Delete").HasAttribute("disabled"));
    }

    [Fact]
    public async Task DeleteButton_ThroughConfirm_RemovesNonActiveModel()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAuthoringStoreAsync(factory);
        var dialogProvider = ConfigureServices(factory);

        var cut = Render<StudioPage>();
        var deleteButton = FindActionButton(cut, "Delete");
        Assert.False(deleteButton.HasAttribute("disabled"));

        var pendingClick = cut.InvokeAsync(() => deleteButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        Assert.Contains("FJORD", messageBox.Markup);
        var confirmButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Delete");
        await cut.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        var models = await NewPublishService(factory).GetAuthoringModelsAsync();
        Assert.DoesNotContain(models, m => m.Code == "FJORD");
        // "FJORD" is a substring of the seeded "FJORD-STUDIO" model, so assert on an exact-code row
        // match rather than a raw substring search of the whole table markup.
        cut.WaitForAssertion(() => Assert.DoesNotContain(
            cut.FindAll("tbody tr"),
            tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == "FJORD")));
    }
}
