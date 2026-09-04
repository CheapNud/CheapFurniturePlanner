using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
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

    [Fact]
    public async Task SaveModelAsync_OnNewCode_AppendsWithNextSortOrder()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var seed = SeedCatalogue.Load();
            var store = new AuthoringCatalogueStore(factory);
            await store.SeedFromAsync(seed);

            await using var db = await factory.CreateDbContextAsync();
            var priorMaxOrder = await db.AuthoringModels.MaxAsync(m => m.SortOrder);

            var newModel = new FurnitureModel { Code = "ZZZ-NEW-TEST", Name = "Brand New Model" };
            await store.SaveModelAsync(newModel);

            var appendedRow = await db.AuthoringModels.AsNoTracking().SingleAsync(m => m.ModelCode == newModel.Code);
            Assert.Equal(priorMaxOrder + 1, appendedRow.SortOrder);

            var reloaded = await store.LoadAsync();
            Assert.Equal(newModel.Code, reloaded.Models[^1].Code);
        }
    }

    [Fact]
    public async Task LoadAsync_AndLoadModelAsync_OnMalformedJson_ThrowInvalidOperationExceptionNamingTheModel()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            const string modelCode = "CORRUPT-MODEL";
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.AuthoringModels.Add(new AuthoringModelDocument
                {
                    ModelCode = modelCode,
                    SortOrder = 0,
                    BundleJson = "{ not valid json",
                });
                await db.SaveChangesAsync();
            }
            var store = new AuthoringCatalogueStore(factory);

            var loadModelEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadModelAsync(modelCode));
            Assert.Contains(modelCode, loadModelEx.Message);

            var loadEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync());
            Assert.Contains(modelCode, loadEx.Message);
        }
    }

    [Fact]
    public async Task Articles_RoundTripThroughStoreAndLoadAsync()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());

        Assert.Empty(await store.LoadArticlesAsync());
        Assert.Empty((await store.LoadAsync()).Articles);

        var articles = new List<Article>
        {
            new() { Id = 1, AssignedCode = "K7E", ModelCode = "M-A", ElementCode = "E-A", VariantCode = "E-A-FEET:ELEC", Selections = new() { ["FEET"] = "ELEC" } },
            new() { Id = 2, AssignedCode = "ART-DROP", Name = "Dropship pouf", ManualPrice = 79m, SupplierRef = "SUP-X" },
        };
        await store.SaveArticlesAsync(articles);

        var loaded = await store.LoadArticlesAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("ELEC", loaded.Single(a => a.Id == 1).Selections["FEET"]);
        Assert.Equal(79m, loaded.Single(a => a.Id == 2).ManualPrice);
        Assert.Equal(2, (await store.LoadAsync()).Articles.Count);
    }

    [Fact]
    public async Task SaveMastersAsync_DoesNotLeakArticlesIntoMastersDoc()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        await store.SaveArticlesAsync([new() { Id = 1, AssignedCode = "K7E", ModelCode = "M-A", ElementCode = "E-A", VariantCode = "E-A" }]);

        var snapshot = await store.LoadAsync();
        await store.SaveMastersAsync(snapshot);

        // The caller's instance is untouched and the articles doc still holds the article; the
        // masters doc itself must not have swallowed the list (reload keeps exactly one article,
        // not two copies from two docs).
        Assert.Single(snapshot.Articles);
        Assert.Single((await store.LoadAsync()).Articles);
    }

    // MB1 Task 3: AuthoringModelDocument.Version is an optimistic concurrency token. A context that
    // loaded the row before another context's save commits is stale by the time IT saves - EF
    // compares its tracked ORIGINAL Version against the row's CURRENT value and throws
    // DbUpdateConcurrencyException when they no longer match. This needs no real thread race: the
    // staleness comes from load order, not from literal simultaneous execution.
    [Fact]
    public async Task SaveModelAsync_StaleWrite_ThrowsConcurrencyException()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var seed = SeedCatalogue.Load();
            var store = new AuthoringCatalogueStore(factory);
            await store.SeedFromAsync(seed);
            var modelCode = seed.Models[0].Code;

            // The stale context reads the row before anyone else touches it.
            await using var staleDb = await factory.CreateDbContextAsync();
            var staleRow = await staleDb.AuthoringModels.FirstAsync(m => m.ModelCode == modelCode);

            // A second editor saves through the real store - Version moves from 0 to 1.
            await store.SaveModelAsync(new FurnitureModel { Code = modelCode, Name = "Edited by someone else" });

            // The stale context's own save now conflicts: its tracked original Version (0) no
            // longer matches the row's current value (1).
            staleRow.BundleJson = "{}";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
        }
    }

    // SaveModelAsync always re-reads fresh inside its own call before mutating (see the class
    // comment on SaveOrThrowFriendlyConflictAsync), so it can never itself be the STALE side of a
    // race against a caller-external write within a single call - only a genuinely concurrent write
    // landing strictly between its own read and its own save would trigger its catch block, which
    // isn't forceable deterministically without a test-only hook into production code. The test
    // above proves the mechanism SaveOrThrowFriendlyConflictAsync relies on (the token really does
    // throw DbUpdateConcurrencyException on a stale save); SaveOrThrowFriendlyConflictAsync's own
    // catch-and-rewrap is a two-line pass-through, verified by inspection.
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
