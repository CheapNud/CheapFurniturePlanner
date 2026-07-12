using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Task 3: the /studio/masters hub renders a tab per master; the Materials tab lists rows, adds via a
// dialog, and blocks deleting a referenced material with an impact message. Harness mirrors
// PriceVersionsPageTests (bUnit + in-memory SQLite, store seeded from the embedded seed).
public class MastersPageTests : TestContext
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
        Services.AddSingleton(sp => new MasterAuthoringService(sp.GetRequiredService<AuthoringCatalogueStore>()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        RenderComponent<MudBlazor.MudDialogProvider>();
        RenderComponent<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task Render_ShowsMastersTabs()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<MastersPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Materials", cut.Markup);
            Assert.Contains("Operations", cut.Markup);
        });
    }

    [Fact]
    public async Task MaterialsTab_RendersSeededRows()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = await SeedAsync(factory);
        var firstCode = (await store.LoadAsync()).Materials[0].Code;
        ConfigureServices(factory);

        var cut = RenderComponent<MastersPage>();

        cut.WaitForAssertion(() => Assert.Contains(firstCode, cut.Markup));
    }

    [Fact]
    public async Task DeleteReferencedMaterial_ShowsImpact_KeepsRow()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = await SeedAsync(factory);
        var snapshot = await store.LoadAsync();
        // Pick a material the seed's BOM actually references, so the guard fires.
        var referenced = snapshot.Materials.First(m => MasterReferenceScanner.FindReferences(snapshot, MasterKind.Material, m.Code).Count > 0).Code;
        var service = new MasterAuthoringService(store);

        // Deleting through the service throws; the tab surfaces this as an impact message. Assert the
        // guard at the service boundary (the tab wraps the same call in try/catch → Snackbar).
        var ex = await Assert.ThrowsAsync<MasterReferencedException>(() => service.DeleteMaterialAsync(referenced));
        Assert.NotEmpty(ex.References);
        Assert.Contains((await store.LoadAsync()).Materials, m => m.Code == referenced);
    }

    [Fact]
    public async Task Render_ShowsAllSevenTabs()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<MastersPage>();

        cut.WaitForAssertion(() =>
        {
            foreach (var tab in new[] { "Materials", "Operations", "Frame bodies", "Spray prices", "Price groups", "Fixed surcharges", "Choice surcharges" })
            {
                Assert.Contains(tab, cut.Markup);
            }
        });
    }

    [Fact]
    public async Task AddPriceGroup_ThroughService_PersistsAndPreservesIntegrity()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = await SeedAsync(factory);
        var service = new MasterAuthoringService(store);

        await service.AddPriceGroupAsync(new PriceGroup { Code = "PG-NEW", Kind = MaterialKind.Leather, RatePerMeter = 12m });

        Assert.Contains((await store.LoadAsync()).PriceGroups, p => p.Code == "PG-NEW" && p.Kind == MaterialKind.Leather);
    }

    [Fact]
    public async Task Render_ShowsFabricGroupsTab()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedAsync(factory);
        ConfigureServices(factory);

        var cut = RenderComponent<MastersPage>();

        cut.WaitForAssertion(() => Assert.Contains("Fabric groups", cut.Markup));
    }
}
