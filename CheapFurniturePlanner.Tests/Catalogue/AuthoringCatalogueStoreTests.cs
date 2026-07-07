using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Catalogue;

// Phase 5 Task 1: the persisted authoring catalogue document store — the crux is that
// splitting a full snapshot into a masters doc + per-model docs and reassembling it via
// LoadAsync() must be byte-lossless against the original embedded seed.
public class AuthoringCatalogueStoreTests
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

    [Fact]
    public async Task SeedFromAsync_ThenLoadAsync_IsByteLosslessAgainstEmbeddedSeed()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var seed = SeedCatalogue.Load();
            var store = new AuthoringCatalogueStore(factory);

            await store.SeedFromAsync(seed);
            var reassembled = await store.LoadAsync();

            var freshSeed = SeedCatalogue.Load();
            reassembled.Version = "";
            reassembled.ContentHash = "";
            freshSeed.Version = "";
            freshSeed.ContentHash = "";

            Assert.Equal(CanonicalJson.Serialize(freshSeed), CanonicalJson.Serialize(reassembled));
        }
    }

    [Fact]
    public async Task SaveModelAsync_ThenLoadModelAsync_RoundTripsFjchElement()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var seed = SeedCatalogue.Load();
            var model = seed.Models.Single(m => m.Elements.Any(e => e.Code == "FJCH"));
            var store = new AuthoringCatalogueStore(factory);

            await store.SaveModelAsync(model);
            var loaded = await store.LoadModelAsync(model.Code);

            Assert.NotNull(loaded);
            Assert.Equal(CanonicalJson.Serialize(model), CanonicalJson.Serialize(loaded));
            Assert.Contains(loaded!.Elements, e => e.Code == "FJCH");
        }
    }

    [Fact]
    public async Task IsSeededAsync_FalseBeforeSeed_TrueAfterSeed()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var store = new AuthoringCatalogueStore(factory);

            Assert.False(await store.IsSeededAsync());

            await store.SeedFromAsync(SeedCatalogue.Load());

            Assert.True(await store.IsSeededAsync());
        }
    }

    [Fact]
    public async Task ModelCodesAsync_ReturnsSeededCodesInOrder()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var seed = SeedCatalogue.Load();
            var store = new AuthoringCatalogueStore(factory);

            await store.SeedFromAsync(seed);
            var codes = await store.ModelCodesAsync();

            Assert.Equal(seed.Models.Select(m => m.Code), codes);
        }
    }

    [Fact]
    public async Task DeleteModelAsync_RemovesOneModel_LeavesOthersIntact()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var seed = SeedCatalogue.Load();
            Assert.True(seed.Models.Count > 1, "Seed must contain more than one model for this test to be meaningful.");
            var store = new AuthoringCatalogueStore(factory);
            await store.SeedFromAsync(seed);
            var codeToDelete = seed.Models[0].Code;

            await store.DeleteModelAsync(codeToDelete);
            var codes = await store.ModelCodesAsync();

            Assert.DoesNotContain(codeToDelete, codes);
            Assert.Equal(seed.Models.Count - 1, codes.Count);
        }
    }

    [Fact]
    public async Task SaveModelAsync_OnExistingCode_UpdatesRatherThanDuplicates()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var seed = SeedCatalogue.Load();
            var store = new AuthoringCatalogueStore(factory);
            await store.SeedFromAsync(seed);
            var model = seed.Models[0];
            var renamed = new FurnitureModel { Code = model.Code, Name = model.Name + " (renamed)", Elements = model.Elements };

            await store.SaveModelAsync(renamed);
            var codes = await store.ModelCodesAsync();
            var loaded = await store.LoadModelAsync(model.Code);

            Assert.Equal(seed.Models.Count, codes.Count);
            Assert.Equal(1, codes.Count(c => c == model.Code));
            Assert.Equal(renamed.Name, loaded!.Name);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
