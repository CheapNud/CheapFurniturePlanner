using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 6: /service/new creates a ticket via ServiceTicketService.CreateTicketAsync, picking a
// consumer/order through PartyService/OrderEntryService. CreateOrderAsync/GetOrderAsync never
// touch the catalogue, so the harness only needs bare ICatalogueSource/PinnedCatalogueProvider
// instances to satisfy OrderEntryService's constructor - no seeded catalogue required, unlike
// OrderEntryPageTests.
public class ServiceIntakePageTests : TestContext
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static async Task<(IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection)> NewFactoryAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        await using (var migrateContext = new FurniturePlannerContext(options))
        {
            await migrateContext.Database.MigrateAsync();
        }
        return (new TestDbContextFactory(options), connection);
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser who)
    {
        var photoRoot = Path.Combine(Path.GetTempPath(), "sv1-intake-tests", Guid.NewGuid().ToString("N"));
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
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task Render_ShowsConsumerSelect_AndCreateButton()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        ConfigureServices(factory, new FakeCurrentUser("office-1", Roles.Office));

        var cut = Render<ServiceIntakePage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Consumer", cut.Markup);
            Assert.Contains("Create ticket", cut.Markup);
        });
    }

    [Fact]
    public async Task SelectingOrder_WithOpenTicket_ShowsDuplicateWarning()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        int consumerId, orderId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var consumer = new Consumer { Name = "Jansen" };
            db.Consumers.Add(consumer);
            var seller = new Seller { Name = "Shop", Multiplier = 1m };
            db.Sellers.Add(seller);
            await db.SaveChangesAsync();
            var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = "BE" };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            consumerId = consumer.Id;
            orderId = order.Id;
        }
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var seedingService = new ServiceTicketService(factory, office);
        await seedingService.CreateTicketAsync(consumerId, orderId, "seat sags", null, ServiceFlow.Undecided, []);
        ConfigureServices(factory, office);

        var cut = Render<ServiceIntakePage>();
        var consumerSelect = cut.FindComponents<MudBlazor.MudSelect<int?>>()[0];
        await cut.InvokeAsync(() => consumerSelect.Instance.ValueChanged.InvokeAsync(consumerId));

        var orderSelect = cut.FindComponents<MudBlazor.MudSelect<int?>>()[1];
        await cut.InvokeAsync(() => orderSelect.Instance.ValueChanged.InvokeAsync(orderId));

        cut.WaitForAssertion(() => Assert.Contains("already has 1 open service ticket", cut.Markup));
    }
}
