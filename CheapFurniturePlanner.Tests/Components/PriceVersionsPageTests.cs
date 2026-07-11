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

// Task 4: the Price Versions studio page lists published catalogue versions with a computed
// status chip, flags unpublished master edits with a warning banner, and lets an operator publish
// a new dated version through a small date-picker dialog. Harness mirrors StudioPageTests (bUnit +
// in-memory SQLite) and PriceVersionServiceTests (seed + mark-Active + RepublishAsync baseline).
public class PriceVersionsPageTests : TestContext
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

    // Seeds the authoring store from the embedded seed, marks every model Active, and publishes
    // once (v1, effective now) - the same baseline PriceVersionServiceTests builds, so the page
    // starts from "one version already current, no pending changes".
    private static async Task<AuthoringCatalogueStore> SeedAndPublishAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var model in SeedCatalogue.Load().Models)
            {
                db.ModelStates.Add(new ModelStateRecord { ModelCode = model.Code, State = TradeItemState.Active });
            }
            await db.SaveChangesAsync();
        }
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        await publish.RepublishAsync();
        return store;
    }

    private static PriceVersionService NewPriceVersionService(IDbContextFactory<FurniturePlannerContext> factory, AuthoringCatalogueStore store)
    {
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        return new PriceVersionService(factory, publish);
    }

    // bUnit renders each RenderComponent<T>() call as its own root in the render tree, so the
    // PublishVersionDialog opened via DialogService.ShowAsync shows up as a descendant of the
    // MudDialogProvider root, NOT of the page under test - mirrors StudioPageTests.
    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new AuthoringCatalogueStore(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton<ICatalogueSource, DbCatalogueSource>();
        Services.AddSingleton(sp => new CataloguePublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<ICatalogueSource>()));
        Services.AddSingleton(sp => new ModelPublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<CataloguePublishService>(), sp.GetRequiredService<ICatalogueSource>(), sp.GetRequiredService<AuthoringCatalogueStore>()));
        Services.AddSingleton(sp => new PriceVersionService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<ModelPublishService>()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var dialogProvider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        return dialogProvider;
    }

    [Fact]
    public async Task Render_AfterSeedAndPublish_ListsVersionRow_WithEffectiveChip()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAndPublishAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<PriceVersionsPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("1", cut.FindAll("tbody tr td").Select(td => td.TextContent.Trim()));
            Assert.Contains(PriceVersionStatus.Effective.ToString(), cut.Markup);
        });
        var chip = cut.FindComponents<MudChip<string>>().Single();
        Assert.Contains(PriceVersionStatus.Effective.ToString(), chip.Markup);
    }

    [Fact]
    public async Task Render_AfterSeedAndPublish_NoPendingBanner()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAndPublishAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<PriceVersionsPage>();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Unpublished price changes", cut.Markup));
    }

    [Fact]
    public async Task Render_AfterMasterEdit_ShowsPendingChangesBanner()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = await SeedAndPublishAsync(factory);
        var masters = await store.LoadAsync();
        masters.Materials[0] = masters.Materials[0] with { UnitCost = masters.Materials[0].UnitCost + 1m };
        await store.SaveMastersAsync(masters);
        ConfigureServices(factory);

        var cut = RenderComponent<PriceVersionsPage>();

        cut.WaitForAssertion(() => Assert.Contains("Unpublished price changes", cut.Markup));
    }

    [Fact]
    public async Task PublishNewVersion_ThroughDialog_AddsVersionRow_AndClearsBanner()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = await SeedAndPublishAsync(factory);
        var masters = await store.LoadAsync();
        masters.Materials[0] = masters.Materials[0] with { UnitCost = masters.Materials[0].UnitCost + 1m };
        await store.SaveMastersAsync(masters);
        var dialogProvider = ConfigureServices(factory);

        var cut = RenderComponent<PriceVersionsPage>();
        cut.WaitForAssertion(() => Assert.Contains("Unpublished price changes", cut.Markup));

        var publishButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Publish new version");

        // PublishAsync awaits dialogRef.Result, which only resolves once the dialog closes - awaiting
        // the click itself here would deadlock the test; fire it and drive the dialog instead,
        // mirroring StudioPageTests' NewModel_Blank_CreatesModel.
        var pendingClick = cut.InvokeAsync(() => publishButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<PublishVersionDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<PublishVersionDialog>();

        var confirmButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Publish");
        await dialog.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        var priceVersions = NewPriceVersionService(factory, store);
        cut.WaitForAssertion(() => Assert.Equal(2, priceVersions.ListVersionsAsync().GetAwaiter().GetResult().Count));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Unpublished price changes", cut.Markup));
    }
}
