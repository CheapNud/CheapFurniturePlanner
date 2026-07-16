using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Task 5: /studio/articles - a standalone-article editor (Add/Edit/Delete via StandaloneArticleDialog)
// plus a read-only catalogue-backed list. Harness mirrors MastersPageTests (bUnit + in-memory SQLite,
// store seeded from the embedded seed) with the publish-chain services ArticleAuthoringService needs,
// same wiring as ArticleAuthoringServiceTests' NewHarnessAsync.
public class StudioArticlesPageTests : TestContext
{
    private const string Studio = "FJORD-STUDIO";   // Draft model in the seed; elements FS2/FS3/FSCH

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

    private async Task<AuthoringCatalogueStore> SeedAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        return store;
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new AuthoringCatalogueStore(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton<ICatalogueSource>(sp => new DbCatalogueSource(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new CataloguePublishService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), sp.GetRequiredService<ICatalogueSource>()));
        Services.AddSingleton(sp => new ModelPublishService(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(),
            sp.GetRequiredService<CataloguePublishService>(),
            sp.GetRequiredService<ICatalogueSource>(),
            sp.GetRequiredService<AuthoringCatalogueStore>()));
        Services.AddSingleton(sp => new ArticleAuthoringService(sp.GetRequiredService<AuthoringCatalogueStore>(), sp.GetRequiredService<ModelPublishService>()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        RenderComponent<MudBlazor.MudDialogProvider>();
        RenderComponent<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task Render_ShowsBothSections()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<StudioArticlesPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Standalone articles", cut.Markup);
            Assert.Contains("Catalogue articles", cut.Markup);
        });
    }

    [Fact]
    public async Task StandaloneAdd_ThroughService_AppearsInTable()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = await SeedAsync(factory);
        var service = new ArticleAuthoringService(store, new ModelPublishService(
            factory,
            new CataloguePublishService(factory, new DbCatalogueSource(factory)),
            new DbCatalogueSource(factory),
            store));
        await service.AddStandaloneAsync(new Article { AssignedCode = "ART-DROP", Name = "Pouf", ManualPrice = 79m, SupplierRef = "SUP-X", State = TradeItemState.Active });
        ConfigureServices(factory);

        var cut = RenderComponent<StudioArticlesPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("ART-DROP", cut.Markup);
            Assert.Contains("SUP-X", cut.Markup);
        });
    }

    [Fact]
    public async Task CatalogueBackedRow_RendersReadOnly_WithModelLink()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = await SeedAsync(factory);
        var publish = new ModelPublishService(
            factory,
            new CataloguePublishService(factory, new DbCatalogueSource(factory)),
            new DbCatalogueSource(factory),
            store);
        var service = new ArticleAuthoringService(store, publish);
        var selections = new Dictionary<string, string> { ["DEPTH"] = "STD" };
        await service.AssignAsync(Studio, "FS2", "FS2-DEPTH:STD", selections, "K7E");
        ConfigureServices(factory);

        var cut = RenderComponent<StudioArticlesPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("K7E", cut.Markup);
            Assert.Contains($"href=\"/studio/{Studio}\"", cut.Markup);
        });
    }
}
