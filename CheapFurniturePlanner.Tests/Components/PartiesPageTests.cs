using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 5: the /parties page lists Sellers and Consumers, each add/edit/delete wired to
// PartyService, following the P2a MastersPage pattern (self-load, dialog plumbing, delete-guard
// surfaced as a Snackbar). Harness mirrors MastersPageTests (bUnit + in-memory SQLite).
public class PartiesPageTests : TestContext
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

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public void Render_ShowsBothSections()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        ConfigureServices(factory);

        var cut = Render<PartiesPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Sellers", cut.Markup);
            Assert.Contains("Consumers", cut.Markup);
        });
    }

    [Fact]
    public async Task SellerAdd_ThroughService_AppearsInTable()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var parties = new PartyService(factory);
        await parties.AddSellerAsync("Alpha", 1.1m);
        ConfigureServices(factory);

        var cut = Render<PartiesPage>();

        cut.WaitForAssertion(() => Assert.Contains("Alpha", cut.Markup));
    }

    [Fact]
    public async Task DeleteSellerWithOrders_ThrowsAtService()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var parties = new PartyService(factory);
        var seller = await parties.AddSellerAsync("Beta", 1m);
        var consumer = await parties.AddConsumerAsync("Gamma", "gamma@example.com");
        var orders = new OrderEntryService(factory, new DbCatalogueSource(factory), new PinnedCatalogueProvider(factory),
            new ProductionUnitService(factory, new FakeCurrentUser("office-1", Roles.Office)));
        await orders.CreateOrderAsync(seller.Id, consumer.Id, "EUN");

        // Deleting through the service throws; the page surfaces this as a Snackbar. Assert the
        // guard at the service boundary (mirrors MastersPageTests.DeleteReferencedMaterial_ShowsImpact_KeepsRow).
        await Assert.ThrowsAsync<InvalidOperationException>(() => parties.DeleteSellerAsync(seller.Id));
        Assert.Contains(await parties.SellersAsync(), s => s.Id == seller.Id);
    }
}
