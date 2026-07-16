using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Configurator;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
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

    // -- Task 3: configured-element lines --

    // FJ2's default configuration (DEPTH:STD, MECH:NONE, STITCH:PLAIN, default AQUA fabric colour) — the
    // same seed element ProductionIdentityServiceTests/ConfigurationResolverTests exercise.
    private static (Element Element, Dictionary<string, string> Selections, string? FabricColorCode) Fj2Default(CatalogueSnapshot snapshot)
    {
        var element = snapshot.Models.SelectMany(m => m.Elements).Single(e => e.Code == "FJ2");
        var selections = ConfigurationResolver.DefaultSelections(element);
        var fabricColorCode = ConfigurationResolver.DefaultFabricColorCode(element, snapshot);
        return (element, selections, fabricColorCode);
    }

    // Computes the exact composed variant code the service's own ResolveIdentity will produce, via the
    // public ProductionIdentityResolver (same MaterialResolution + VariantCode.From derivation), so a
    // naming assigned under this code is guaranteed to be the one the bridge hits.
    private static string ComposedVariantCode(CatalogueSnapshot snapshot, string modelCode, Dictionary<string, string> selections, string? fabricColorCode)
    {
        var config = new ProductConfiguration(modelCode, [new ElementSelection("FJ2", 1, selections, fabricColorCode)]);
        return ProductionIdentityResolver.Resolve(snapshot, config, new Dictionary<string, string>(), TradeItemState.Active)[0].VariantCode;
    }

    [Fact]
    public async Task AddConfiguredLine_PricesAndStampsBridge()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var (_, selections, fabricColorCode) = Fj2Default(SeedCatalogue.Load());
        var variantCode = ComposedVariantCode(SeedCatalogue.Load(), "FJORD", selections, fabricColorCode);

        await harness.Publish.SetStateAsync("FJORD", TradeItemState.Draft);
        await harness.Articles.AssignAsync("FJORD", "FJ2", variantCode, selections, "K7E");
        await harness.Publish.SetStateAsync("FJORD", TradeItemState.Active);

        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUW");
        await harness.Orders.AddConfiguredLineAsync(order.Id, "FJORD", "FJ2", selections, fabricColorCode, 1);

        var line = Assert.Single((await harness.Orders.GetOrderAsync(order.Id))!.Lines);
        Assert.Equal("K7E", line.AssignedCode);
        Assert.NotNull(line.ArticleId);
        Assert.True(line.UnitPrice > 0);
        Assert.False(string.IsNullOrEmpty(line.VariantCode));
        Assert.Equal(OrderLineKind.ConfiguredElement, line.Kind);
        Assert.Equal(selections, CanonicalJson.Deserialize<Dictionary<string, string>>(line.SelectionsJson));
    }

    [Fact]
    public async Task AddConfiguredLine_UnnamedVariant_FallsBackToComposedCode()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var (_, selections, fabricColorCode) = Fj2Default(SeedCatalogue.Load());
        var variantCode = ComposedVariantCode(SeedCatalogue.Load(), "FJORD", selections, fabricColorCode);
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUW");

        await harness.Orders.AddConfiguredLineAsync(order.Id, "FJORD", "FJ2", selections, fabricColorCode, 1);

        var line = Assert.Single((await harness.Orders.GetOrderAsync(order.Id))!.Lines);
        Assert.Null(line.AssignedCode);
        Assert.Null(line.ArticleId);
        Assert.Equal(variantCode, line.VariantCode);
    }

    [Fact]
    public async Task AddConfiguredLine_QuantityBelowOne_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var (_, selections, fabricColorCode) = Fj2Default(SeedCatalogue.Load());
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUW");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Orders.AddConfiguredLineAsync(order.Id, "FJORD", "FJ2", selections, fabricColorCode, 0));

        var reloaded = await harness.Orders.GetOrderAsync(order.Id);
        Assert.Empty(reloaded!.Lines);
        Assert.Null(reloaded.PinnedCatalogueVersion);
    }

    // The headline: a mid-flight price change never moves a pinned order — not on the original add,
    // not on a reconfigure against the same configuration, only a brand-new order sees the new price.
    [Fact]
    public async Task PinnedOrder_DoesNotMoveWhenPricesChange()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var (_, selections, fabricColorCode) = Fj2Default(SeedCatalogue.Load());

        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUW");
        await harness.Orders.AddConfiguredLineAsync(order.Id, "FJORD", "FJ2", selections, fabricColorCode, 1);
        var originalLine = Assert.Single((await harness.Orders.GetOrderAsync(order.Id))!.Lines);
        var originalPrice = originalLine.UnitPrice;

        var masters = new MasterAuthoringService(harness.Store);
        var glue = (await harness.Store.LoadAsync()).Materials.Single(m => m.Code == "GLUE");
        await masters.UpdateMaterialAsync("GLUE", glue with { UnitCost = glue.UnitCost + 10m });
        await harness.Publish.RepublishAsync();

        var stillLine = Assert.Single((await harness.Orders.GetOrderAsync(order.Id))!.Lines);
        Assert.Equal(originalPrice, stillLine.UnitPrice);

        await harness.Orders.ReconfigureLineAsync(order.Id, stillLine.Id, selections, fabricColorCode);
        var afterReconfigure = Assert.Single((await harness.Orders.GetOrderAsync(order.Id))!.Lines);
        Assert.Equal(originalPrice, afterReconfigure.UnitPrice);

        var newOrder = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUW");
        await harness.Orders.AddConfiguredLineAsync(newOrder.Id, "FJORD", "FJ2", selections, fabricColorCode, 1);
        var newLine = Assert.Single((await harness.Orders.GetOrderAsync(newOrder.Id))!.Lines);
        Assert.NotEqual(originalPrice, newLine.UnitPrice);
    }

    // EUN's rounding (2 final decimals) happens to double cleanly for FJ2's default configuration, so
    // an exact 2x assertion on the stored preview price is reliable here (verified against the real
    // engine, not assumed).
    [Fact]
    public async Task PriceConfiguration_AppliesSellerMultiplier()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var (element, selections, fabricColorCode) = Fj2Default(SeedCatalogue.Load());
        var sellerOne = await harness.Parties.AddSellerAsync("Base Reseller", 1m);
        var sellerTwo = await harness.Parties.AddSellerAsync("Double Reseller", 2m);
        var orderOne = await harness.Orders.CreateOrderAsync(sellerOne.Id, harness.Consumer.Id, "EUN");
        var orderTwo = await harness.Orders.CreateOrderAsync(sellerTwo.Id, harness.Consumer.Id, "EUN");

        var priceOne = await harness.Orders.PriceConfigurationAsync(orderOne, "FJORD", element.Code, selections, fabricColorCode);
        var priceTwo = await harness.Orders.PriceConfigurationAsync(orderTwo, "FJORD", element.Code, selections, fabricColorCode);

        Assert.True(priceOne.IsSuccess);
        Assert.True(priceTwo.IsSuccess);
        Assert.Equal(priceOne.Breakdown!.Elements[0].ElementTotal * 2m, priceTwo.Breakdown!.Elements[0].ElementTotal);
    }

    [Fact]
    public async Task AddConfiguredLine_InvalidConfiguration_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewOrderHarnessAsync(factory);
        var (element, selections, fabricColorCode) = Fj2Default(SeedCatalogue.Load());
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "BE"); // not a catalogue market

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Orders.AddConfiguredLineAsync(order.Id, "FJORD", element.Code, selections, fabricColorCode, 1));
    }
}
