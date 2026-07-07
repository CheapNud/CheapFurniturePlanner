using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Catalogue;

// Phase 5 Task 2: proves the store-sourced publish path (ModelPublishService.RepublishAsync now
// reads AuthoringCatalogueStore.LoadAsync() instead of SeedCatalogue.Load()) produces a byte-identical
// published catalogue to publishing the embedded seed directly - i.e. re-pointing the read did not
// change what gets published.
public class AuthoringPublishIntegrationTests
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

    [Fact]
    public async Task RepublishAsync_StoreSourced_ProducesSameContentHash_AsPublishingSeedDirectly()
    {
        // DB 1: store-sourced path - seed the authoring store from the embedded seed, mark every
        // model Active, then republish through ModelPublishService (which now reads the store).
        var (storeFactory, storeConn) = NewFactory();
        using var _1 = storeConn;
        var store = new AuthoringCatalogueStore(storeFactory);
        await store.SeedFromAsync(SeedCatalogue.Load());

        await using (var db = await storeFactory.CreateDbContextAsync())
        {
            foreach (var model in SeedCatalogue.Load().Models)
            {
                db.ModelStates.Add(new ModelStateRecord { ModelCode = model.Code, State = TradeItemState.Active });
            }
            await db.SaveChangesAsync();
        }

        var storeSource = new DbCatalogueSource(storeFactory);
        var storePublishService = new ModelPublishService(storeFactory, new CataloguePublishService(storeFactory, storeSource), storeSource, store);
        await storePublishService.RepublishAsync();

        var storePublished = await storeSource.GetCurrentAsync();

        // DB 2: a completely separate, fresh DB - publish the embedded seed (all models) directly,
        // bypassing the store entirely, to establish the ground truth this must match.
        var (directFactory, directConn) = NewFactory();
        using var _2 = directConn;
        var directSource = new DbCatalogueSource(directFactory);
        var directPublishService = new CataloguePublishService(directFactory, directSource);
        var directResult = await directPublishService.PublishAsync(SeedCatalogue.Load());
        Assert.True(directResult.Success, "Direct publish of the embedded seed must succeed: " + string.Join("; ", directResult.Errors));

        var directPublished = await directSource.GetCurrentAsync();

        Assert.Equal(directPublished.ContentHash, storePublished.ContentHash);
    }
}
