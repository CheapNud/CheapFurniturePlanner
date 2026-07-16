using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 2: OrderEntryService creates numbered draft orders, pins them to the currently effective
// published catalogue version on the first line added (never re-resolving against "current" after
// that), and supports standalone-article lines (quantity/remove/totals). Harness mirrors
// PriceVersionServiceTests: in-memory SQLite, store seeded from the embedded seed, every model
// marked Active, then published once so there is a baseline "current" version.
public class OrderEntryServiceTests
{
    private static (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (new TestDbContextFactory(options), connection);
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed record Harness(
        ModelPublishService Publish,
        AuthoringCatalogueStore Store,
        ArticleAuthoringService Articles,
        PartyService Parties,
        OrderEntryService Orders,
        Seller Seller,
        Consumer Consumer);

    // Seeds the store from the embedded seed, marks every model Active, publishes once (v1, a
    // standalone article available for ordering), and creates one Seller/Consumer via PartyService.
    private static async Task<Harness> NewOrderHarnessAsync(IDbContextFactory<FurniturePlannerContext> factory)
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
        await articles.AddStandaloneAsync(new Article { AssignedCode = "ART-DROP", Name = "Pouf", ManualPrice = 79m, SupplierRef = "SUP-X", State = TradeItemState.Active });
        await publish.RepublishAsync();

        var pinned = new PinnedCatalogueProvider(factory);
        var orders = new OrderEntryService(factory, source, pinned);
        var parties = new PartyService(factory);
        var seller = await parties.AddSellerAsync("Northwind Reseller", 1.2m);
        var consumer = await parties.AddConsumerAsync("Jane Consumer", "jane@example.com");
        return new Harness(publish, store, articles, parties, orders, seller, consumer);
    }

    private static async Task<Article> StandaloneArticleAsync(Harness harness, IDbContextFactory<FurniturePlannerContext> factory)
    {
        var articles = await harness.Store.LoadArticlesAsync();
        return articles.Single(a => a.AssignedCode == "ART-DROP");
    }

    [Fact]
    public async Task CreateOrder_GeneratesSequentialNumbers()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var year = DateTime.UtcNow.Year;

        var first = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");
        var second = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");

        Assert.Equal($"ORD-{year}-0001", first.OrderNumber);
        Assert.Equal($"ORD-{year}-0002", second.OrderNumber);
        Assert.Null(first.PinnedCatalogueVersion);
        Assert.Null(first.PinnedContentHash);
    }

    [Fact]
    public async Task AddStandaloneLine_PinsOrderAndSnapshotsPrice()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var article = await StandaloneArticleAsync(harness, factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");
        var current = await new DbCatalogueSource(factory).GetCurrentAsync();

        await harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 2);

        var reloaded = await harness.Orders.GetOrderAsync(order.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(current.Version, reloaded!.PinnedCatalogueVersion);
        var line = Assert.Single(reloaded.Lines);
        Assert.Equal(79m, line.UnitPrice);
        Assert.Equal(158m, line.LineTotal);
        Assert.Equal("SUP-X", line.SupplierRef);
        Assert.Equal("ART-DROP", line.AssignedCode);
        Assert.Equal(OrderLineKind.StandaloneArticle, line.Kind);
    }

    [Fact]
    public async Task Pin_SurvivesNewVersion()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var article = await StandaloneArticleAsync(harness, factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");

        await harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 1);
        var afterFirstLine = await harness.Orders.GetOrderAsync(order.Id);
        var pinnedVersion = afterFirstLine!.PinnedCatalogueVersion;
        Assert.NotNull(pinnedVersion);

        await harness.Publish.RepublishAsync();
        var newCurrent = await new DbCatalogueSource(factory).GetCurrentAsync();
        Assert.NotEqual(pinnedVersion, newCurrent.Version);

        await harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 1);
        var afterSecondLine = await harness.Orders.GetOrderAsync(order.Id);
        Assert.Equal(pinnedVersion, afterSecondLine!.PinnedCatalogueVersion);
        Assert.Equal(2, afterSecondLine.Lines.Count);

        var secondOrder = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");
        await harness.Orders.AddStandaloneLineAsync(secondOrder.Id, article.Id, 1);
        var secondReloaded = await harness.Orders.GetOrderAsync(secondOrder.Id);
        Assert.Equal(newCurrent.Version, secondReloaded!.PinnedCatalogueVersion);
    }

    [Fact]
    public async Task UpdateQuantity_RecomputesLineTotal()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var article = await StandaloneArticleAsync(harness, factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");
        await harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 1);
        var line = (await harness.Orders.GetOrderAsync(order.Id))!.Lines.Single();

        await harness.Orders.UpdateQuantityAsync(order.Id, line.Id, 5);

        var reloaded = await harness.Orders.GetOrderAsync(order.Id);
        var updated = reloaded!.Lines.Single();
        Assert.Equal(5, updated.Quantity);
        Assert.Equal(395m, updated.LineTotal);
    }

    [Fact]
    public async Task RemoveLine_RenumbersAndKeepsPin()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var article = await StandaloneArticleAsync(harness, factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");
        await harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 1);
        var pinnedVersion = (await harness.Orders.GetOrderAsync(order.Id))!.PinnedCatalogueVersion;
        var line = (await harness.Orders.GetOrderAsync(order.Id))!.Lines.Single();

        await harness.Orders.RemoveLineAsync(order.Id, line.Id);

        var reloaded = await harness.Orders.GetOrderAsync(order.Id);
        Assert.Empty(reloaded!.Lines);
        Assert.Equal(pinnedVersion, reloaded.PinnedCatalogueVersion);
    }

    [Fact]
    public async Task AddStandaloneLine_QuantityBelowOne_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var article = await StandaloneArticleAsync(harness, factory);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 0));
    }

    [Fact]
    public async Task DeleteSellerWithOrders_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Parties.DeleteSellerAsync(harness.Seller.Id));
    }
}
