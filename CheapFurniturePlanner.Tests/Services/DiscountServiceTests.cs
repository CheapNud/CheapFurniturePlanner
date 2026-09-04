using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors PartyServiceTests: in-memory SQLite, migrated schema.
public class DiscountServiceTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

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

    private static DiscountRule ModelRule(int sellerId, string modelCode, decimal ratePercent, string? collectionCode = null) => new()
    {
        SellerId = sellerId,
        Scope = DiscountScope.Model,
        ModelCode = modelCode,
        CollectionCode = collectionCode,
        RatePercent = ratePercent,
    };

    [Fact]
    public async Task AddRule_ThenList_RoundTripsOrdered()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);

        await service.AddRuleAsync(new DiscountRule { SellerId = 1, Scope = DiscountScope.Everything, RatePercent = 5m });
        await service.AddRuleAsync(ModelRule(1, "M1", 10m));

        var rules = await service.RulesForSellerAsync(1);

        Assert.Equal(2, rules.Count);
        Assert.Equal(DiscountScope.Model, rules[0].Scope);
        Assert.Equal(DiscountScope.Everything, rules[1].Scope);
    }

    [Fact]
    public async Task AddRule_XorViolation_BothSet_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRuleAsync(new DiscountRule
        {
            SellerId = 1,
            Scope = DiscountScope.ElementPriceGroup,
            ElementCode = "EA",
            PriceGroupCode = "PGA",
            RatePercent = 10m,
            FixedPrice = 5m,
        }));
    }

    [Fact]
    public async Task AddRule_XorViolation_NeitherSet_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRuleAsync(new DiscountRule
        {
            SellerId = 1,
            Scope = DiscountScope.Model,
            ModelCode = "M1",
        }));
    }

    [Fact]
    public async Task AddRule_FixedPrice_OnNonElementPriceGroupScope_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRuleAsync(new DiscountRule
        {
            SellerId = 1,
            Scope = DiscountScope.Model,
            ModelCode = "M1",
            FixedPrice = 5m,
        }));
    }

    [Fact]
    public async Task AddRule_MissingRequiredScopeKey_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRuleAsync(new DiscountRule
        {
            SellerId = 1,
            Scope = DiscountScope.Model,
            RatePercent = 10m,
        }));
    }

    [Fact]
    public async Task AddRule_ForbiddenScopeKeyPresent_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRuleAsync(new DiscountRule
        {
            SellerId = 1,
            Scope = DiscountScope.Everything,
            ModelCode = "M1",
            RatePercent = 10m,
        }));
    }

    [Fact]
    public async Task AddRule_Duplicate_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);
        await service.AddRuleAsync(ModelRule(1, "M1", 10m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRuleAsync(ModelRule(1, "M1", 20m)));
    }

    [Fact]
    public async Task UpdateRule_EditsValuesOnly_AndRevalidates()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);
        var rule = ModelRule(1, "M1", 10m);
        await service.AddRuleAsync(rule);

        await service.UpdateRuleAsync(rule.Id, 25m, null);

        var rules = await service.RulesForSellerAsync(1);
        var updated = Assert.Single(rules);
        Assert.Equal(25m, updated.RatePercent);
        Assert.Equal("M1", updated.ModelCode);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateRuleAsync(rule.Id, 25m, 5m));
    }

    [Fact]
    public async Task DeleteRule_Removes()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);
        var rule = ModelRule(1, "M1", 10m);
        await service.AddRuleAsync(rule);

        await service.DeleteRuleAsync(rule.Id);

        Assert.Empty(await service.RulesForSellerAsync(1));
    }

    // MB1 Task 3 backstop: mirrors AddRuleAsync's duplicate guard column-for-column, but only as a
    // PARTIAL backstop - see the index comment in FurniturePlannerContext. AddRuleAsync's own
    // Validate() never produces a row with every one of these 8 columns non-null (each scope forces
    // several to stay null), so this raw insert - deliberately bypassing AddRuleAsync/Validate to
    // fill every column - is the only shape that actually trips the index.
    [Fact]
    public async Task RawInsert_IdenticalRuleAcrossAllIndexedColumns_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var rule = new DiscountRule
        {
            SellerId = 1,
            CollectionCode = "COL",
            Scope = DiscountScope.ElementPriceGroup,
            ElementCode = "EA",
            PriceGroupCode = "PGA",
            ModelCode = "M1",
            ModelType = ModelType.Classic,
            MaterialTypeCode = "MT1",
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.DiscountRules.Add(rule);
            await db.SaveChangesAsync();
        }

        await using var dupDb = await factory.CreateDbContextAsync();
        dupDb.DiscountRules.Add(new DiscountRule
        {
            SellerId = rule.SellerId,
            CollectionCode = rule.CollectionCode,
            Scope = rule.Scope,
            ElementCode = rule.ElementCode,
            PriceGroupCode = rule.PriceGroupCode,
            ModelCode = rule.ModelCode,
            ModelType = rule.ModelType,
            MaterialTypeCode = rule.MaterialTypeCode,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => dupDb.SaveChangesAsync());
    }
}
