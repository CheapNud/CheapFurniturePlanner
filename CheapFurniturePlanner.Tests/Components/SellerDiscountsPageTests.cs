using Bunit;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Task 4: the /parties/{SellerId}/discounts page lists a seller's DiscountRules, following the
// P2a MastersPage pattern (self-load, dialog plumbing, delete-guard surfaced as a Snackbar).
// Harness mirrors PartiesPageTests (bUnit + in-memory SQLite).
public class SellerDiscountsPageTests : TestContext
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

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new DiscountService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        RenderComponent<MudBlazor.MudDialogProvider>();
        RenderComponent<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task Render_ShowsSellerRules()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var parties = new PartyService(factory);
        var seller = await parties.AddSellerAsync("Alpha", 1m);
        var discounts = new DiscountService(factory);
        await discounts.AddRuleAsync(new DiscountRule { SellerId = seller.Id, Scope = DiscountScope.Model, ModelCode = "M1", RatePercent = 10m });
        await discounts.AddRuleAsync(new DiscountRule { SellerId = seller.Id, Scope = DiscountScope.Everything, RatePercent = 5m });
        ConfigureServices(factory);

        var cut = RenderComponent<SellerDiscountsPage>(parameters => parameters.Add(p => p.SellerId, seller.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Model", cut.Markup);
            Assert.Contains("Everything", cut.Markup);
            Assert.Contains("10%", cut.Markup);
            Assert.Contains("5%", cut.Markup);
        });
    }

    [Fact]
    public async Task RuleValue_RendersFixedPrice()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var parties = new PartyService(factory);
        var seller = await parties.AddSellerAsync("Beta", 1m);
        var discounts = new DiscountService(factory);
        await discounts.AddRuleAsync(new DiscountRule
        {
            SellerId = seller.Id,
            Scope = DiscountScope.ElementPriceGroup,
            ElementCode = "EA",
            PriceGroupCode = "PGA",
            FixedPrice = 42m,
        });
        ConfigureServices(factory);

        var cut = RenderComponent<SellerDiscountsPage>(parameters => parameters.Add(p => p.SellerId, seller.Id));

        cut.WaitForAssertion(() => Assert.Contains("42", cut.Markup));
    }

    [Fact]
    public async Task XorViolation_ThrowsAtService()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var discounts = new DiscountService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => discounts.AddRuleAsync(new DiscountRule
        {
            SellerId = 1,
            Scope = DiscountScope.ElementPriceGroup,
            ElementCode = "EA",
            PriceGroupCode = "PGA",
            RatePercent = 10m,
            FixedPrice = 5m,
        }));
    }
}
