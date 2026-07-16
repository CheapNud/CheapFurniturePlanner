using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Task 6: the order entry UI — /orders lists orders and opens a creation dialog, /orders/{id} hosts
// the tabbable article/element control on Draft orders and freezes once Placed/Cancelled. Harness
// mirrors OrderEntryServiceTests (in-memory SQLite, seed + mark every model Active + republish) with
// services registered as singletons the way PartiesPageTests/MastersPageTests do.
public class OrderEntryPageTests : TestContext
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
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

    private sealed record Harness(
        AuthoringCatalogueStore Store,
        DbCatalogueSource Source,
        ModelPublishService Publish,
        ArticleAuthoringService Articles,
        PartyService Parties,
        OrderEntryService Orders,
        Seller Seller,
        Consumer Consumer);

    // Seeds the store from the embedded seed, marks every model Active, publishes once (a baseline
    // "current" version), and creates one Seller/Consumer via PartyService.
    private static async Task<Harness> SeedAsync(IDbContextFactory<FurniturePlannerContext> factory)
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
        var articles = new ArticleAuthoringService(store, publish);
        var parties = new PartyService(factory);
        var pinned = new PinnedCatalogueProvider(factory);
        var orders = new OrderEntryService(factory, source, pinned);
        var seller = await parties.AddSellerAsync("Northwind Reseller", 1.2m);
        var consumer = await parties.AddConsumerAsync("Jane Consumer", "jane@example.com");
        await publish.RepublishAsync();
        return new Harness(store, source, publish, articles, parties, orders, seller, consumer);
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton<ICatalogueSource>(sp => new DbCatalogueSource(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new PinnedCatalogueProvider(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new OrderEntryService(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(),
            sp.GetRequiredService<ICatalogueSource>(),
            sp.GetRequiredService<PinnedCatalogueProvider>()));
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        RenderComponent<MudBlazor.MudDialogProvider>();
        RenderComponent<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task Render_Draft_ShowsTabsAndHeader()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await SeedAsync(factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUN");
        ConfigureServices(factory);

        var cut = RenderComponent<OrderEntryPage>(parameters => parameters.Add(p => p.OrderId, order.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(order.OrderNumber, cut.Markup);
            Assert.Contains("Element", cut.Markup);
            Assert.Contains("Article", cut.Markup);
        });
    }

    [Fact]
    public async Task StandaloneAdd_ThroughService_ShowsLineAndPin()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await SeedAsync(factory);
        await harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "ART-DROP", Name = "Pouf", ManualPrice = 79m, SupplierRef = "SUP-X", State = TradeItemState.Active });
        await harness.Publish.RepublishAsync();
        var article = (await harness.Store.LoadArticlesAsync()).Single(a => a.AssignedCode == "ART-DROP");
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUN");
        await harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 2);
        var reloaded = await harness.Orders.GetOrderAsync(order.Id);
        ConfigureServices(factory);

        var cut = RenderComponent<OrderEntryPage>(parameters => parameters.Add(p => p.OrderId, order.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("ART-DROP", cut.Markup);
            Assert.NotNull(reloaded!.PinnedCatalogueVersion);
            Assert.Contains(reloaded.PinnedCatalogueVersion!, cut.Markup);
        });
    }

    [Fact]
    public async Task PlacedOrder_RendersFrozen()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await SeedAsync(factory);
        await harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "ART-DROP", Name = "Pouf", ManualPrice = 79m, SupplierRef = "SUP-X", State = TradeItemState.Active });
        await harness.Publish.RepublishAsync();
        var article = (await harness.Store.LoadArticlesAsync()).Single(a => a.AssignedCode == "ART-DROP");
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUN");
        await harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 1);
        await harness.Orders.PlaceAsync(order.Id);
        ConfigureServices(factory);

        var cut = RenderComponent<OrderEntryPage>(parameters => parameters.Add(p => p.OrderId, order.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(cut.FindComponents<MudBlazor.MudTabs>(), _ => true);
            Assert.Empty(cut.FindComponents<MudBlazor.MudNumericField<int>>());
        });
    }

    [Fact]
    public async Task OrdersList_ShowsCreatedOrder()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await SeedAsync(factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUN");
        ConfigureServices(factory);

        var cut = RenderComponent<OrdersPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(order.OrderNumber, cut.Markup);
            Assert.Contains("Draft", cut.Markup);
        });
    }
}
