using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Configurator;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 6: the order page surfaces the Create invoice button once an order is Placed, and swaps it
// for a link to the invoice once one exists. Harness mirrors OrderProductionIntegrationTests (full
// OrderEntryService graph) plus a real InvoicingService and a MarketVatRate seed for the order's
// market (EUW, 21% - "BE" isn't a market in the seed catalogue's snapshot).
public class OrderInvoiceButtonTests : TestContext
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
        InvoicingService Invoicing,
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
            db.MarketVatRates.Add(new MarketVatRate { MarketCode = "EUW", RatePercent = 21m });
            await db.SaveChangesAsync();
        }
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        var articles = new ArticleAuthoringService(store, publish);
        var parties = new PartyService(factory);
        var pinned = new PinnedCatalogueProvider(factory);
        var productionUnits = new ProductionUnitService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var orders = new OrderEntryService(factory, source, pinned, productionUnits);
        var invoicing = new InvoicingService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var seller = await parties.AddSellerAsync("Northwind Reseller", 1.2m);
        var consumer = await parties.AddConsumerAsync("Jane Consumer", "jane@example.com");
        await publish.RepublishAsync();
        return new Harness(store, source, publish, articles, parties, orders, invoicing, seller, consumer);
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, InvoicingService invoicing)
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
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new DiscountService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(invoicing);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    // FJ2's default configuration — mirrors OrderEntryServiceTests/OrderEntryPageTests.Fj2Default.
    private static (Element Element, Dictionary<string, string> Selections, string? FabricColorCode) Fj2Default(CatalogueSnapshot snapshot)
    {
        var element = snapshot.Models.SelectMany(m => m.Elements).Single(e => e.Code == "FJ2");
        var selections = ConfigurationResolver.DefaultSelections(element);
        var fabricColorCode = ConfigurationResolver.DefaultFabricColorCode(element, snapshot);
        return (element, selections, fabricColorCode);
    }

    [Fact]
    public async Task Placed_ShowsCreateInvoice_Draft_DoesNot_Invoiced_ShowsLink()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await SeedAsync(factory);
        var (_, selections, fabricColorCode) = Fj2Default(SeedCatalogue.Load());
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUW");
        await harness.Orders.AddConfiguredLineAsync(order.Id, "FJORD", "FJ2", selections, fabricColorCode, 1);
        ConfigureServices(factory, harness.Invoicing);

        // draft order rendered -> no "Create invoice"
        var draftCut = Render<OrderEntryPage>(parameters => parameters.Add(p => p.OrderId, order.Id));
        draftCut.WaitForAssertion(() => Assert.DoesNotContain("Create invoice", draftCut.Markup));

        // place it (service) + re-render -> button present
        await harness.Orders.PlaceAsync(order.Id);
        var placedCut = Render<OrderEntryPage>(parameters => parameters.Add(p => p.OrderId, order.Id));
        placedCut.WaitForAssertion(() => Assert.Contains("Create invoice", placedCut.Markup));

        // invoice via service + re-render -> button gone, invoice number link present
        var invoice = await harness.Invoicing.CreateInvoiceAsync(order.Id);
        var invoicedCut = Render<OrderEntryPage>(parameters => parameters.Add(p => p.OrderId, order.Id));
        invoicedCut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Create invoice", invoicedCut.Markup);
            Assert.Contains(invoice.InvoiceNumber, invoicedCut.Markup);
        });
    }
}
