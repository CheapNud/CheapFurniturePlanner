using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 3: PriceVersionService lists published versions with a computed status (Effective / Scheduled /
// Superseded), detects unpublished edits to the working masters against the latest published content
// (version-independent), and publishes a new dated version. Harness mirrors ModelPublishGateTests /
// AuthoringPublishIntegrationTests: in-memory SQLite, store seeded from the embedded seed, every model
// marked Active, then published once so there is a baseline "current" version.
public class PriceVersionServiceTests
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

    private sealed record Harness(ModelPublishService Publish, AuthoringCatalogueStore Store, PriceVersionService PriceVersions);

    // Seeds the store from the embedded seed, marks every model Active, and publishes once (v1,
    // effective now) so tests start from a known "one version already current" baseline.
    private static async Task<Harness> NewHarnessAsync(IDbContextFactory<FurniturePlannerContext> factory)
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
        await publish.RepublishAsync();
        var priceVersions = new PriceVersionService(factory, publish);
        return new Harness(publish, store, priceVersions);
    }

    [Fact]
    public async Task ListVersions_LabelsEffectiveScheduledSuperseded()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        await harness.Publish.RepublishAsync(DateTime.UtcNow.AddDays(30));

        var versions = await harness.PriceVersions.ListVersionsAsync();
        Assert.Equal(PriceVersionStatus.Scheduled, versions.Single(v => v.Version == "2").Status);
        Assert.Equal(PriceVersionStatus.Effective, versions.Single(v => v.Version == "1").Status);
    }

    [Fact]
    public async Task HasPendingChanges_FalseAfterPublish_TrueAfterMasterEdit()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        Assert.False(await harness.PriceVersions.HasPendingChangesAsync());

        var masters = await harness.Store.LoadAsync();
        masters.Materials[0] = masters.Materials[0] with { UnitCost = masters.Materials[0].UnitCost + 1m };
        await harness.Store.SaveMastersAsync(masters);

        Assert.True(await harness.PriceVersions.HasPendingChangesAsync());
    }

    [Fact]
    public async Task PublishNewVersion_StampsDate_AndClearsPending()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        var masters = await harness.Store.LoadAsync();
        masters.Materials[0] = masters.Materials[0] with { UnitCost = 999m };
        await harness.Store.SaveMastersAsync(masters);
        var effective = DateTime.UtcNow.AddDays(7);

        await harness.PriceVersions.PublishNewVersionAsync(effective);

        Assert.False(await harness.PriceVersions.HasPendingChangesAsync());
        await using var db = factory.CreateDbContext();
        Assert.Contains(db.PublishedCatalogues, c => c.EffectiveDate == effective);
    }
}
