using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Configurator;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using CheapHelpers.Services.DataExchange.Pdf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 7: the address/supplier ripple across order entry and service intake/tickets - the order
// header's delivery-address picker (draft-only, locks once placed), the supplier report's picker
// (replacing the old raw numeric-id field), and the intake page's visit-address prefill. Harnesses
// mirror OrderEntryPageTests/ServiceTicketPageTests/ServiceIntakePageTests exactly.
public class AddressUiRippleTests : TestContext
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static async Task<(IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection)> NewFactoryAsync(bool seedRoles = false)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        await using (var migrateContext = new FurniturePlannerContext(options))
        {
            await migrateContext.Database.MigrateAsync();
            if (seedRoles) { await RoleSeeder.SeedAsync(migrateContext); }
        }
        return (new TestDbContextFactory(options), connection);
    }

    // --- OrderEntryPage: delivery-address picker ---

    private sealed record OrderHarness(
        AuthoringCatalogueStore Store,
        DbCatalogueSource Source,
        ModelPublishService Publish,
        ArticleAuthoringService Articles,
        PartyService Parties,
        OrderEntryService Orders,
        Seller Seller,
        Consumer Consumer);

    // Mirrors OrderEntryPageTests.SeedAsync: seed store, mark every model Active, publish once.
    private static async Task<OrderHarness> SeedOrderAsync(IDbContextFactory<FurniturePlannerContext> factory)
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
        return new OrderHarness(store, source, publish, articles, parties, orders, seller, consumer);
    }

    private void ConfigureOrderServices(IDbContextFactory<FurniturePlannerContext> factory)
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
        Services.AddSingleton(sp => new DiscountService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new InvoicingService(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), new FakeCurrentUser("office-1", Roles.Office)));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task OrderHeader_AddressSelect_PersistsAndLocksAfterPlace()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var harness = await SeedOrderAsync(factory);
        // First entry auto-defaults (mirrors CreateOrderAsync's default-address lookup), so the
        // order starts pointed at "Home" - the test then switches it to "Work" through the UI.
        var home = await harness.Parties.AddDeliveryAddressAsync(harness.Consumer.Id, "Home",
            new Address { Street = "Kerkstraat", Number = "1", PostalCode = "9000", City = "Gent" });
        var work = await harness.Parties.AddDeliveryAddressAsync(harness.Consumer.Id, "Work",
            new Address { Street = "Marktplein", Number = "2", PostalCode = "9000", City = "Gent" });
        await harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "ART-DROP", Name = "Pouf", ManualPrice = 79m, State = TradeItemState.Active });
        await harness.Publish.RepublishAsync();
        var article = (await harness.Store.LoadArticlesAsync()).Single(a => a.AssignedCode == "ART-DROP");
        var order = await harness.Orders.CreateOrderAsync(harness.Seller.Id, harness.Consumer.Id, "EUN");
        await harness.Orders.AddStandaloneLineAsync(order.Id, article.Id, 1);
        var beforeChange = await harness.Orders.GetOrderAsync(order.Id);
        Assert.Equal(home.AddressId, beforeChange!.DeliveryAddressId);
        ConfigureOrderServices(factory);

        var cut = Render<OrderEntryPage>(parameters => parameters.Add(p => p.OrderId, order.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<MudBlazor.MudSelect<int?>>()));

        var addressSelect = cut.FindComponent<MudBlazor.MudSelect<int?>>();
        Assert.False(addressSelect.Instance.Disabled);
        await cut.InvokeAsync(() => addressSelect.Instance.ValueChanged.InvokeAsync(work.Id));

        await cut.WaitForAssertionAsync(async () =>
        {
            var reloaded = await harness.Orders.GetOrderAsync(order.Id);
            Assert.Equal(work.AddressId, reloaded!.DeliveryAddressId);
        });

        await harness.Orders.PlaceAsync(order.Id);
        var placedCut = Render<OrderEntryPage>(parameters => parameters.Add(p => p.OrderId, order.Id));
        placedCut.WaitForAssertion(() =>
        {
            var placedSelect = placedCut.FindComponent<MudBlazor.MudSelect<int?>>();
            Assert.True(placedSelect.Instance.Disabled);
        });
    }

    // --- ServiceTicketPage / SupplierReportSection: supplier picker ---

    private static async Task<int> SeedConsumerAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var consumer = new Consumer { Name = "Jansen" };
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        return consumer.Id;
    }

    private void ConfigureServiceTicketServices(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser who)
    {
        var photoRoot = Path.Combine(Path.GetTempPath(), "ad1-ticket-tests", Guid.NewGuid().ToString("N"));
        var pdfRoot = Path.Combine(Path.GetTempPath(), "ad1-ticket-pdf-tests", Guid.NewGuid().ToString("N"));
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(who);
        Services.AddSingleton(sp => new ServiceTicketService(factory, who));
        Services.AddSingleton(new ServicePhotoStore(photoRoot));
        Services.AddSingleton(sp => new SupplierReportPdf(factory, new PdfExportService(new PdfTemplateService()), pdfRoot));
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), who));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task SupplierSection_PickerSaves()
    {
        var (factory, conn) = await NewFactoryAsync(seedRoles: true);
        using var _ = conn;
        var consumerId = await SeedConsumerAsync(factory);
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var parties = new PartyService(factory, office);
        var supplier = await parties.AddSupplierAsync("SUP-1", "Sup One Wholesale");
        var seedingService = new ServiceTicketService(factory, office);
        var ticket = await seedingService.CreateTicketAsync(consumerId, null, "lamp flickers", null, ServiceFlow.External, []);

        ConfigureServiceTicketServices(factory, office);
        var cut = Render<ServiceTicketPage>(p => p.Add(x => x.Id, ticket.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<MudBlazor.MudSelect<int?>>()));

        var supplierSelect = cut.FindComponent<MudBlazor.MudSelect<int?>>();
        await cut.InvokeAsync(() => supplierSelect.Instance.ValueChanged.InvokeAsync(supplier.Id));

        var saveButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save");
        await cut.InvokeAsync(() => saveButton.Click());

        await cut.WaitForAssertionAsync(async () =>
        {
            var reloaded = await seedingService.GetAsync(ticket.Id);
            Assert.Equal(supplier.Id, reloaded!.SupplierReport!.SupplierId);
        });
    }

    // --- ServiceIntakePage: visit-address prefill ---

    private void ConfigureIntakeServices(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser who)
    {
        var photoRoot = Path.Combine(Path.GetTempPath(), "ad1-intake-tests", Guid.NewGuid().ToString("N"));
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(who);
        Services.AddSingleton(sp => new ServiceTicketService(factory, who));
        Services.AddSingleton(new ServicePhotoStore(photoRoot));
        Services.AddSingleton<ICatalogueSource>(sp => new DbCatalogueSource(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new PinnedCatalogueProvider(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new ProductionUnitService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), who));
        Services.AddSingleton(sp => new OrderEntryService(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(),
            sp.GetRequiredService<ICatalogueSource>(),
            sp.GetRequiredService<PinnedCatalogueProvider>(),
            sp.GetRequiredService<ProductionUnitService>()));
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), who));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task Intake_VisitAddress_Prefills()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var parties = new PartyService(factory, office);
        var consumer = await parties.AddConsumerAsync("Jansen", null);
        var bookEntry = await parties.AddDeliveryAddressAsync(consumer.Id, "Home",
            new Address { Street = "Main Street", Number = "12", PostalCode = "1000", City = "Springfield" });
        ConfigureIntakeServices(factory, office);

        var cut = Render<ServiceIntakePage>();
        var consumerSelect = cut.FindComponents<MudBlazor.MudSelect<int?>>()[0];
        await cut.InvokeAsync(() => consumerSelect.Instance.ValueChanged.InvokeAsync(consumer.Id));

        cut.WaitForAssertion(() =>
        {
            var visitAddressField = cut.FindComponents<MudBlazor.MudTextField<string>>()
                .Single(f => f.Instance.Label == "Visit address (optional)");
            Assert.Equal(bookEntry.Address!.ToOneLine(), visitAddressField.Instance.Value);
        });
    }
}
