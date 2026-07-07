using AngleSharp.Dom;
using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Exercises the modellenkamer as the model-list gatekeeper: it lists the authoring model set (the
// embedded seed catalogue) with each model's release state, and its Release/Discontinue actions are
// state-gated the same way ModelPublishService itself gates them. Runs against a real
// ModelPublishService over in-memory SQLite, mirroring FurnitureConfigPanelTests/DbCatalogueSourceTests.
public class ModellenkamerPageTests : TestContext
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

    // The state machine now republishes the Active-only snapshot on every transition, so a service
    // built for out-of-band setup needs a real CataloguePublishService + ICatalogueSource.
    private static ModelPublishService NewPublishService(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var source = new DbCatalogueSource(factory);
        return new ModelPublishService(factory, new CataloguePublishService(factory, source), source);
    }

    // bUnit renders each RenderComponent<T>() call as its own root in the render tree, so the
    // MudMessageBox that ShowMessageBoxAsync opens shows up as a descendant of the MudDialogProvider
    // root, NOT of the page under test - callers must query the returned handle, not `cut`.
    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton<ICatalogueSource, DbCatalogueSource>();
        Services.AddSingleton(sp => new CataloguePublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<ICatalogueSource>()));
        Services.AddSingleton(sp => new ModelPublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<CataloguePublishService>(), sp.GetRequiredService<ICatalogueSource>()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        // MudTable's Release/Discontinue confirmations render via MudDialogProvider; MudSelect-style
        // overlays elsewhere in the app rely on MudPopoverProvider - both need to be present.
        var dialogProvider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        return dialogProvider;
    }

    // The table now lists more than one authoring model, so buttons must be scoped to a model's own
    // row (identified by its Code cell) rather than picked globally by their label.
    private static IElement FindActionButton(IRenderedComponent<ModellenkamerPage> cut, string text, string modelCode = "FJORD")
    {
        var row = cut.FindAll("tbody tr").Single(tr => tr.QuerySelectorAll("td").Any(td => td.TextContent.Trim() == modelCode));
        return row.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);
    }

    [Fact]
    public void Render_ListsSeedModel_WithDraftStateAndReleaseEnabled()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        ConfigureServices(factory);

        var cut = RenderComponent<ModellenkamerPage>();

        Assert.Contains("FJORD", cut.Markup);
        Assert.Contains("Fjord", cut.Markup);
        Assert.Contains(TradeItemState.Draft.ToString(), cut.Markup);

        var releaseButton = FindActionButton(cut, "Release");
        var discontinueButton = FindActionButton(cut, "Discontinue");
        Assert.False(releaseButton.HasAttribute("disabled"));
        Assert.True(discontinueButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Render_AfterOutOfBandRelease_ShowsActiveState_WithDiscontinueEnabled()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        // Released out-of-band (as a second operator/tab would) before the page ever loads, so this
        // proves the initial load reflects persisted state rather than a page-local assumption.
        await NewPublishService(factory).ReleaseAsync("FJORD");
        ConfigureServices(factory);

        var cut = RenderComponent<ModellenkamerPage>();

        Assert.Contains(TradeItemState.Active.ToString(), cut.Markup);
        var releaseButton = FindActionButton(cut, "Release");
        var discontinueButton = FindActionButton(cut, "Discontinue");
        Assert.True(releaseButton.HasAttribute("disabled"));
        Assert.False(discontinueButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task ClickingRelease_ThroughConfirm_TransitionsDraftToActive_AndRebadges()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<ModellenkamerPage>();
        var releaseButton = FindActionButton(cut, "Release");

        // ShowMessageBoxAsync suspends until the rendered MudMessageBox dialog is dismissed, so
        // awaiting the click itself here would deadlock the test; fire it and drive the dialog instead.
        var pendingClick = cut.InvokeAsync(() => releaseButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        Assert.Contains("FJORD", messageBox.Markup);
        var confirmButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Release");
        await cut.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(TradeItemState.Active.ToString(), cut.Markup);
            Assert.True(FindActionButton(cut, "Release").HasAttribute("disabled"));
            Assert.False(FindActionButton(cut, "Discontinue").HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task ClickingDiscontinue_ThroughConfirm_TransitionsActiveToDiscontinued()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await NewPublishService(factory).ReleaseAsync("FJORD");
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<ModellenkamerPage>();
        var discontinueButton = FindActionButton(cut, "Discontinue");

        var pendingClick = cut.InvokeAsync(() => discontinueButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        var confirmButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Discontinue");
        await cut.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(TradeItemState.Discontinued.ToString(), cut.Markup);
            Assert.True(FindActionButton(cut, "Release").HasAttribute("disabled"));
            Assert.True(FindActionButton(cut, "Discontinue").HasAttribute("disabled"));
        });
    }
}
