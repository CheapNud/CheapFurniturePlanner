using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Configurator;
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
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 6: the order page's promised-delivery picker. Harness mirrors OrderEntryPageTests exactly
// (same in-memory SQLite seed + full service graph including ProductionUnitService).
public class OrderPromiseUiTests : TestContext
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
        var parties = new PartyService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var pinned = new PinnedCatalogueProvider(factory);
        var productionUnits = new ProductionUnitService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var orders = new OrderEntryService(factory, source, pinned, productionUnits);
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
        Services.AddSingleton(sp => new ProductionUnitService(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), new FakeCurrentUser("office-1", Roles.Office)));
        Services.AddSingleton(sp => new OrderEntryService(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(),
            sp.GetRequiredService<ICatalogueSource>(),
            sp.GetRequiredService<PinnedCatalogueProvider>(),
            sp.GetRequiredService<ProductionUnitService>()));
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), new FakeCurrentUser("office-1", Roles.Office)));
        Services.AddSingleton(sp => new FirmService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), new FakeCurrentUser("office-1", Roles.Office)));
        Services.AddSingleton(sp => new DiscountService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new InvoicingService(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), new FakeCurrentUser("office-1", Roles.Office)));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task PromisePicker_PersistsOnDraft_AndStaysEnabledWhenPlaced()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await SeedAsync(factory);
        await harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "ART-DROP", Name = "Pouf", ManualPrice = 79m, SupplierRef = "SUP-X", State = TradeItemState.Active });
        await harness.Publish.RepublishAsync();
        var article = (await harness.Store.LoadArticlesAsync()).Single(a => a.AssignedCode == "ART-DROP");
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUN");
        await harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 1);
        ConfigureServices(factory);

        var cut = Render<OrderEntryPage>(parameters => parameters.Add(p => p.OrderId, order.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<MudBlazor.MudDatePicker>()));

        // Set a date on the still-Draft order — picker calls straight through to the service.
        var draftDate = new DateTime(2026, 8, 1);
        var draftPicker = cut.FindComponent<MudBlazor.MudDatePicker>();
        Assert.False(draftPicker.Instance.Disabled);
        await cut.InvokeAsync(() => draftPicker.Instance.DateChanged.InvokeAsync(draftDate));
        await cut.WaitForAssertionAsync(async () =>
        {
            var reloaded = await harness.Orders.GetOrderAsync(order.Id);
            Assert.Equal(draftDate, reloaded!.PromisedDeliveryDate);
        });

        // Place the order via the service (not the UI button) and force the page to re-fetch —
        // unlike its Draft-only neighbors, the picker must stay enabled once Placed.
        await harness.Orders.PlaceAsync(order.Id);
        cut.Render(parameters => parameters.Add(p => p.OrderId, order.Id));
        cut.WaitForAssertion(() => Assert.Contains("Placed", cut.Markup));

        var placedPicker = cut.FindComponent<MudBlazor.MudDatePicker>();
        Assert.False(placedPicker.Instance.Disabled);

        var placedDate = new DateTime(2026, 8, 15);
        await cut.InvokeAsync(() => placedPicker.Instance.DateChanged.InvokeAsync(placedDate));
        await cut.WaitForAssertionAsync(async () =>
        {
            var reloaded = await harness.Orders.GetOrderAsync(order.Id);
            Assert.Equal(placedDate, reloaded!.PromisedDeliveryDate);
        });
    }
}
