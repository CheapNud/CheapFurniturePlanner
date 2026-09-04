using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CheapFurniturePlanner.Tests.PostgresSmoke;

// MB-1 Task 6: end-to-end parity smoke against a real Postgres server. This is the one place in
// the whole solution where running EF migrations against a live database is sanctioned (see
// CLAUDE.md's Database Migration Rules) - every database it touches is a throwaway one the test
// itself creates and drops, never a shared dev/staging/prod instance.
//
// Self-skips (returns early, no assertion runs, xunit reports it as a pass) whenever
// PLANNER_PG_TEST is unset - this repo's xunit is 2.9.3, which has no built-in dynamic Skip; the
// early-return idiom is the documented workaround (see the csproj comment).
public class PostgresProviderSmokeTests
{
    private static readonly FakeCurrentUser OfficeUser = new("office-1", Roles.Office);

    private sealed class SmokeDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    // Minimal stand-in for CheapFurniturePlanner.Tests' FakeCurrentUser - not worth a project
    // reference (and its bunit/coverlet baggage) for five lines.
    private sealed class FakeCurrentUser(string? userId, params string[] roles) : ICurrentUser
    {
        public Task<string?> UserIdAsync() => Task.FromResult(userId);
        public Task<string> DisplayNameAsync() => Task.FromResult(userId ?? "anonymous");
        public Task<bool> IsInRoleAsync(string role) => Task.FromResult(roles.Contains(role));
    }

    private static readonly Dictionary<string, string> StdSelections = new() { ["DEPTH"] = "STD", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" };

    [Fact]
    public async Task DualProvider_EndToEnd_MatchesSqliteBehavior()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("PLANNER_PG_TEST");
        if (string.IsNullOrWhiteSpace(baseConnectionString)) { return; } // skip silently - no server configured

        // Guid, not a Date.Now/ticks stamp - parallel CI runs against the same server must never
        // collide on the database name. Lowercase hex only, so it never needs quoting for casing.
        var databaseName = $"planner_test_{Guid.NewGuid():N}";
        var testConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName }.ConnectionString;

        await using (var admin = new NpgsqlConnection(baseConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<FurniturePlannerContext>()
                .UseNpgsql(testConnectionString, npgsql => npgsql.MigrationsAssembly(DbProviderConfigurator.PostgresMigrationsAssembly))
                .Options;
            await using (var migrate = new FurniturePlannerContext(options))
            {
                await migrate.Database.MigrateAsync();
            }

            var factory = new SmokeDbContextFactory(options);
            await RunFlowAsync(factory);
        }
        finally
        {
            // The pooled connections above are still open server-side even after DbContext
            // disposal (Npgsql keeps them warm in the pool) - clear them before dropping so the
            // FORCE option isn't the only thing standing between this and a "database is being
            // accessed by other users" failure.
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(baseConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task RunFlowAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        // --- seed a minimal published catalogue via the real authoring store, mirroring
        // ProductionUnitServiceTests.NewOrderHarnessAsync ---
        var store = new AuthoringCatalogueStore(factory);
        var seed = SeedCatalogue.Load();
        await store.SeedFromAsync(seed);
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var model in seed.Models)
            {
                db.ModelStates.Add(new ModelStateRecord { ModelCode = model.Code, State = TradeItemState.Active });
            }
            await db.SaveChangesAsync();
        }
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        await publish.RepublishAsync();

        var parties = new PartyService(factory, OfficeUser);
        var pinned = new PinnedCatalogueProvider(factory);
        var units = new ProductionUnitService(factory, OfficeUser, pinned);
        var orders = new OrderEntryService(factory, source, pinned, units);
        var discounts = new DiscountService(factory);

        var seller = await parties.AddSellerAsync("Smoke Reseller", 1.2m);
        var consumer = await parties.AddConsumerAsync("Smoke Consumer", "smoke@example.com");

        // --- order -> place -> unit (real services all the way) ---
        // "EUW" - the Fjord demo catalogue's market code for a configured line (mirrors
        // ProductionUnitServiceTests.Fj2Default / OrderProductionIntegrationTests; "BE" only works
        // for standalone-article lines, which skip the catalogue market check).
        var orderOne = await orders.CreateOrderAsync(seller.Id, consumer.Id, "EUW");
        await orders.AddConfiguredLineAsync(orderOne.Id, "FJORD", "FJ2", StdSelections, "AQUA-BLUE", 1);
        await orders.PlaceAsync(orderOne.Id);

        // T2 landmine: a UTC instant (Order.CreatedAt) and a calendar date (Order.PromisedDeliveryDate,
        // pinned to "timestamp without time zone" - see NpgsqlTimestampPinTests) both round-trip.
        var promised = new DateTime(2026, 12, 24, 0, 0, 0, DateTimeKind.Unspecified);
        await orders.SetPromisedDeliveryDateAsync(orderOne.Id, promised);
        var reloadedOne = await orders.GetOrderAsync(orderOne.Id);
        Assert.NotNull(reloadedOne);
        Assert.True(reloadedOne!.CreatedAt > DateTime.UtcNow.AddMinutes(-5)); // wrote+read a real instant, no throw
        Assert.Equal(promised, reloadedOne.PromisedDeliveryDate); // wrote+read a real calendar date, no throw

        // Max-suffix order numbering: a second order in the same year must land one past the first,
        // not just "count + 1" (see OrderEntryService.CreateOrderAsync's comment).
        var orderTwo = await orders.CreateOrderAsync(seller.Id, consumer.Id, "EUW");
        var suffixOne = int.Parse(reloadedOne.OrderNumber[^4..]);
        var suffixTwo = int.Parse(orderTwo.OrderNumber[^4..]);
        Assert.Equal(suffixOne + 1, suffixTwo);

        // --- in-house mark -> finish/backflush (real services) ---
        await parties.MarkModelInHouseAsync("FJORD");
        var spawnedUnits = await units.UnitsForOrderAsync(orderOne.Id);
        var unit = Assert.Single(spawnedUnits);
        await units.FinishAsync(unit.Id);

        await using (var check = await factory.CreateDbContextAsync())
        {
            var stocks = await check.MaterialStocks.ToDictionaryAsync(s => (s.Kind, s.Code, s.HardnessCode), s => s.Amount);
            Assert.Equal(-1m, stocks[(MaterialKind.Frame, "FBX", "")]);
            Assert.Equal(-2m, stocks[(MaterialKind.Foam, "FM-STD", "")]);
            Assert.Equal(-3.0m, stocks[(MaterialKind.Cotton, "COT-STD", "")]);
            Assert.Equal(-4.0m, stocks[(MaterialKind.Fabric, "AQUA-BLUE", "")]);
            Assert.Equal(-4m, stocks[(MaterialKind.Misc, "GLUE", "")]);

            var movements = await check.MaterialMovements.ToListAsync();
            Assert.Equal(5, movements.Count);
            Assert.All(movements, m => Assert.Equal(MaterialMovementType.Backflush, m.Type));

            // T4b landmine: the unique index on (Kind, Code, HardnessCode) genuinely collides on
            // Postgres for two rows sharing the "" sentinel (unlike NULL, which never collides under
            // either provider) - raw-inserting a second Frame/FBX/"" row on top of the one FinishAsync
            // just wrote must throw.
            await using var race = await factory.CreateDbContextAsync();
            race.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FBX", HardnessCode = "", Amount = 1m, UpdatedAt = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => race.SaveChangesAsync());
        }

        // T3 landmine: the (SellerId, IdentityKey) backstop index genuinely collides on Postgres too
        // - mirrors DiscountServiceTests.RawInsert_ConcurrentEverythingScopeRule_SameIdentityKey_Throws.
        var rule = new DiscountRule { SellerId = seller.Id, Scope = DiscountScope.Everything, RatePercent = 5m };
        await discounts.AddRuleAsync(rule);
        await using (var raceDb = await factory.CreateDbContextAsync())
        {
            raceDb.DiscountRules.Add(new DiscountRule { SellerId = seller.Id, Scope = DiscountScope.Everything, RatePercent = 9m, IdentityKey = rule.IdentityKey });
            await Assert.ThrowsAsync<DbUpdateException>(() => raceDb.SaveChangesAsync());
        }
    }
}
