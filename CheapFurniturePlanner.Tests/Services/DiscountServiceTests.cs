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

    // MB1 review fix: the old 8-nullable-column index this test used to exercise is gone (replaced by
    // the (SellerId, IdentityKey) index below) - the old index could never catch this shape. Everything
    // scope leaves every one of those 6 columns null (per Validate()), and NULL <> NULL under both
    // providers, so two such rows never collided. This reproduces the TOCTOU race AddRuleAsync's own
    // AnyAsync check cannot close on its own: the first rule goes through AddRuleAsync normally
    // (computing its IdentityKey), then a second context inserts a rule that would compute the same
    // key (same seller, same scope, everything else null) but skips the duplicate check - simulating
    // "both contexts passed the check before either committed". The new IdentityKey collapses the
    // nulls to a fixed sentinel, so the raw insert now collides on (SellerId, IdentityKey).
    [Fact]
    public async Task RawInsert_ConcurrentEverythingScopeRule_SameIdentityKey_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new DiscountService(factory);
        var rule = new DiscountRule { SellerId = 1, Scope = DiscountScope.Everything, RatePercent = 5m };
        await service.AddRuleAsync(rule);

        await using var raceDb = await factory.CreateDbContextAsync();
        raceDb.DiscountRules.Add(new DiscountRule
        {
            SellerId = 1,
            Scope = DiscountScope.Everything,
            RatePercent = 9m,
            IdentityKey = rule.IdentityKey,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => raceDb.SaveChangesAsync());
    }

    // MB1 review fix: pre-MB1 databases have every DiscountRule row at the IdentityKey default (""),
    // which the filtered index deliberately excludes so the migration itself never fails on them.
    // BackfillIdentityKeysAsync (called once at startup, right after Database.Migrate()) computes real
    // keys for those rows. Two genuinely distinct rules backfill to distinct keys with no collision
    // handling needed.
    [Fact]
    public async Task BackfillIdentityKeysAsync_ComputesKeysForPreExistingRows()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            seedDb.DiscountRules.AddRange(
                new DiscountRule { SellerId = 1, Scope = DiscountScope.Everything, RatePercent = 5m },
                new DiscountRule { SellerId = 1, Scope = DiscountScope.Model, ModelCode = "M1", RatePercent = 10m });
            await seedDb.SaveChangesAsync();
        }

        var service = new DiscountService(factory);
        await service.BackfillIdentityKeysAsync();

        await using var checkDb = await factory.CreateDbContextAsync();
        var keys = await checkDb.DiscountRules.Select(r => r.IdentityKey).ToListAsync();
        Assert.All(keys, k => Assert.False(string.IsNullOrEmpty(k)));
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // MB1 review fix: a genuine identity collision among pre-existing rows would mean the app guard's
    // own race actually fired at some point in this database's history (vanishingly rare - see the
    // BackfillIdentityKeysAsync comment). The backfill must not lose either row or crash on its own
    // SaveChangesAsync - it appends the row id to the losing key so both rows survive with distinct keys.
    [Fact]
    public async Task BackfillIdentityKeysAsync_CollidingPreExistingRows_KeepsBothRowsWithDistinctKeys()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            seedDb.DiscountRules.AddRange(
                new DiscountRule { SellerId = 1, Scope = DiscountScope.Everything, RatePercent = 5m },
                new DiscountRule { SellerId = 1, Scope = DiscountScope.Everything, RatePercent = 9m });
            await seedDb.SaveChangesAsync();
        }

        var service = new DiscountService(factory);
        await service.BackfillIdentityKeysAsync();

        await using var checkDb = await factory.CreateDbContextAsync();
        var rules = await checkDb.DiscountRules.ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.All(rules, r => Assert.False(string.IsNullOrEmpty(r.IdentityKey)));
        Assert.Equal(2, rules.Select(r => r.IdentityKey).Distinct().Count());
    }
}
