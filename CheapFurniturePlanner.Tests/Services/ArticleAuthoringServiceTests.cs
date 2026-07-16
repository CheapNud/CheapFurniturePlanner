using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 3: ArticleAuthoringService replaces VariantNamingService - naming a variant now creates a
// catalogue-backed Article carrying full provenance, still Draft-gated; standalone articles (legacy/
// dropship) get their own CRUD; PruneForElement/DeleteForModel are the cascade hooks the structure-
// authoring services call. Harness mirrors ElementAuthoringServiceTests/PriceVersionServiceTests:
// in-memory SQLite, store seeded from the embedded seed, explicit ModelStates rows.
public class ArticleAuthoringServiceTests
{
    private const string Studio = "FJORD-STUDIO";   // Draft model in the seed; elements FS2/FS3/FSCH
    private const string Active = "FJORD";           // Active model in the seed; elements FJ2/FJ3/FJCH

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

    private sealed record Harness(ArticleAuthoringService Articles, ModelPublishService Publish, AuthoringCatalogueStore Store);

    private static async Task<Harness> NewHarnessAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        return new Harness(new ArticleAuthoringService(store, publish), publish, store);
    }

    // Seeds the two demo model states exactly as Program.cs does on first run: FJORD Active,
    // FJORD-STUDIO Draft.
    private static async Task SeedModelStatesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Active, State = TradeItemState.Active });
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
        await db.SaveChangesAsync();
    }

    // -- naming API parity (was VariantNamingServiceTests) --

    [Fact]
    public async Task AssignAsync_CreatesCatalogueBackedArticle_AndNamesForModelReturnsIt()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        var selections = new Dictionary<string, string> { ["DEPTH"] = "STD" };

        await harness.Articles.AssignAsync(Studio, "FS2", "FS2-DEPTH:STD", selections, "K7E");

        var names = await harness.Articles.NamesForModelAsync(Studio);
        Assert.Equal("K7E", names["FS2-DEPTH:STD"]);

        var articles = await harness.Store.LoadArticlesAsync();
        var article = Assert.Single(articles);
        Assert.Equal("K7E", article.AssignedCode);
        Assert.Equal(Studio, article.ModelCode);
        Assert.Equal("FS2", article.ElementCode);
        Assert.Equal("FS2-DEPTH:STD", article.VariantCode);
        Assert.Equal(selections, article.Selections);
        Assert.Equal(TradeItemState.Draft, article.State);
    }

    [Fact]
    public async Task AssignAsync_BlankCode_DeletesTheArticle()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        var selections = new Dictionary<string, string> { ["DEPTH"] = "STD" };
        await harness.Articles.AssignAsync(Studio, "FS2", "FS2-DEPTH:STD", selections, "K7E");

        await harness.Articles.AssignAsync(Studio, "FS2", "FS2-DEPTH:STD", selections, "   ");

        Assert.Empty(await harness.Articles.NamesForModelAsync(Studio));
        Assert.Empty(await harness.Store.LoadArticlesAsync());
    }

    [Fact]
    public async Task AssignAsync_NonDraftModel_ThrowsNamingFrozen()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await Assert.ThrowsAsync<NamingFrozenException>(() =>
            harness.Articles.AssignAsync(Active, "FJ2", "FJ2-DEPTH:STD", new Dictionary<string, string> { ["DEPTH"] = "STD" }, "K7E"));
    }

    [Fact]
    public async Task AssignAsync_SameVariantTwice_UpdatesNotDuplicates()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        var selections = new Dictionary<string, string> { ["DEPTH"] = "STD" };
        await harness.Articles.AssignAsync(Studio, "FS2", "FS2-DEPTH:STD", selections, "K7E");

        await harness.Articles.AssignAsync(Studio, "FS2", "FS2-DEPTH:STD", selections, "18F");

        var articles = await harness.Store.LoadArticlesAsync();
        var article = Assert.Single(articles);
        Assert.Equal("18F", article.AssignedCode);
        var names = await harness.Articles.NamesForModelAsync(Studio);
        Assert.Equal("18F", names["FS2-DEPTH:STD"]);
    }

    // -- standalone CRUD --

    [Fact]
    public async Task AddStandalone_Persists_AndRequiresManualPrice()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "DROP-1", ManualPrice = 49.99m, SupplierRef = "SUP-A" });

        var articles = await harness.Store.LoadArticlesAsync();
        var article = Assert.Single(articles);
        Assert.Equal("DROP-1", article.AssignedCode);
        Assert.Equal(49.99m, article.ManualPrice);
        Assert.Equal("SUP-A", article.SupplierRef);
        Assert.False(article.IsCatalogueBacked());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "DROP-2" }));   // no ManualPrice

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "DROP-3", ManualPrice = -1m }));   // negative

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "   ", ManualPrice = 10m }));   // empty code
    }

    [Fact]
    public async Task UpdateAndDeleteStandalone_ById()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        await harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "DROP-1", ManualPrice = 10m });
        var id = Assert.Single(await harness.Store.LoadArticlesAsync()).Id;

        await harness.Articles.UpdateStandaloneAsync(id, new Article { AssignedCode = "DROP-1-B", ManualPrice = 20m });

        var updated = Assert.Single(await harness.Store.LoadArticlesAsync());
        Assert.Equal(id, updated.Id);
        Assert.Equal("DROP-1-B", updated.AssignedCode);
        Assert.Equal(20m, updated.ManualPrice);

        await harness.Articles.DeleteStandaloneAsync(id);

        Assert.Empty(await harness.Store.LoadArticlesAsync());
    }

    [Fact]
    public async Task AddStandalone_WithProvenance_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Articles.AddStandaloneAsync(new Article { AssignedCode = "DROP-1", ManualPrice = 10m, ModelCode = Studio }));
    }

    // -- cascade hooks --

    [Fact]
    public async Task PruneForElement_RemovesExactPrefixMatches_KeepsOthers()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        await harness.Store.SaveArticlesAsync(
        [
            new Article { Id = 1, AssignedCode = "A1", ModelCode = Studio, ElementCode = "FS2", VariantCode = "FS2" },
            new Article { Id = 2, AssignedCode = "A2", ModelCode = Studio, ElementCode = "FS2", VariantCode = "FS2-DEPTH:STD" },
            // Different element ("FS2X"): shares prefix chars with "FS2" but not the "FS2-" separator.
            new Article { Id = 3, AssignedCode = "A3", ModelCode = Studio, ElementCode = "FS2X", VariantCode = "FS2X-DEPTH:STD" },
            new Article { Id = 4, AssignedCode = "A4", ManualPrice = 10m },   // standalone, no provenance
        ]);

        await harness.Articles.PruneForElementAsync(Studio, "FS2");

        var remaining = await harness.Store.LoadArticlesAsync();
        Assert.DoesNotContain(remaining, a => a.Id == 1);
        Assert.DoesNotContain(remaining, a => a.Id == 2);
        Assert.Contains(remaining, a => a.Id == 3);
        Assert.Contains(remaining, a => a.Id == 4);
    }

    [Fact]
    public async Task DeleteForModel_RemovesAllArticlesOfModel_KeepsStandalone()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        await harness.Store.SaveArticlesAsync(
        [
            new Article { Id = 1, AssignedCode = "A1", ModelCode = Studio, ElementCode = "FS2", VariantCode = "FS2" },
            new Article { Id = 2, AssignedCode = "A2", ModelCode = Active, ElementCode = "FJ2", VariantCode = "FJ2" },
            new Article { Id = 3, AssignedCode = "A3", ManualPrice = 10m },   // standalone, no provenance
        ]);

        await harness.Articles.DeleteForModelAsync(Studio);

        var remaining = await harness.Store.LoadArticlesAsync();
        Assert.DoesNotContain(remaining, a => a.Id == 1);
        Assert.Contains(remaining, a => a.Id == 2);
        Assert.Contains(remaining, a => a.Id == 3);
    }
}

// Task 3: VariantNamingAbsorber is a one-time startup conversion - the OE1AbsorbVariantNamings
// migration renames VariantNamings to LegacyVariantNamings; this absorber reads any staged rows,
// converts each into a catalogue-backed Article, and drops the staging table. Idempotent (a fresh DB
// with no staging table is a no-op).
public class VariantNamingAbsorberTests
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

    // Variant codes are "elementCode-DEF:CHOICE-..." or the bare elementCode; element codes cannot
    // contain '-' (the domain's own invariant - see ElementAuthoringService.RequireCodeAndName), so
    // the staged rows here use hyphen-free element codes ("EA"/"EB") rather than the brief's
    // illustrative "E-A"/"E-B", which would themselves mis-parse under that same splitting rule.
    // The migration already renames VariantNamings -> LegacyVariantNamings during Database.Migrate()
    // (run by NewFactory below), so the table already exists here - only the rows need staging.
    [Fact]
    public async Task Absorb_ConvertsLegacyRows_ParsesProvenance_DropsStagingTable()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO LegacyVariantNamings (ModelCode, VariantCode, AssignedCode, CreatedAt, UpdatedAt) VALUES ('M-A', 'EA-FEET:ELEC-__MATERIAL__:LEATHER', 'K7E', '2026-01-01', '2026-01-01');");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO LegacyVariantNamings (ModelCode, VariantCode, AssignedCode, CreatedAt, UpdatedAt) VALUES ('M-A', 'EB', 'PLAIN', '2026-01-01', '2026-01-01');");
            db.ModelStates.Add(new ModelStateRecord { ModelCode = "M-A", State = TradeItemState.Active });
            await db.SaveChangesAsync();
        }
        var store = new AuthoringCatalogueStore(factory);
        var absorber = new VariantNamingAbsorber(factory, store);

        await absorber.AbsorbAsync();

        var articles = await store.LoadArticlesAsync();
        Assert.Equal(2, articles.Count);

        var first = Assert.Single(articles, a => a.VariantCode == "EA-FEET:ELEC-__MATERIAL__:LEATHER");
        Assert.Equal("EA", first.ElementCode);
        Assert.Equal(new Dictionary<string, string> { ["FEET"] = "ELEC", ["__MATERIAL__"] = "LEATHER" }, first.Selections);
        Assert.Equal(TradeItemState.Active, first.State);
        Assert.Equal("K7E", first.AssignedCode);
        Assert.Equal("M-A", first.ModelCode);

        var second = Assert.Single(articles, a => a.VariantCode == "EB");
        Assert.Equal("EB", second.ElementCode);
        Assert.Empty(second.Selections);
        Assert.Equal(TradeItemState.Active, second.State);
        Assert.Equal("PLAIN", second.AssignedCode);

        await using var verify = await factory.CreateDbContextAsync();
        var connection = verify.Database.GetDbConnection();
        await verify.Database.OpenConnectionAsync();
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='LegacyVariantNamings';";
        Assert.Equal(0, Convert.ToInt32(await check.ExecuteScalarAsync()));
    }

    // C1: a KEY's VALUE is free text (PriceGroup.MaterialTypeCode for the synthetic __MATERIAL__
    // selection) and may itself contain a hyphen ("LEATHER-THICK" in the seed). The parser must split
    // only immediately before a "KEY:" segment, not on every '-', or the value gets truncated and a
    // phantom key appears.
    [Fact]
    public async Task Absorb_MaterialTypeCodeWithHyphen_ParsesValueIntact()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO LegacyVariantNamings (ModelCode, VariantCode, AssignedCode, CreatedAt, UpdatedAt) VALUES ('M-A', 'FJCH-DEPTH:STD-__MATERIAL__:LEATHER-THICK', 'K7E', '2026-01-01', '2026-01-01');");
            db.ModelStates.Add(new ModelStateRecord { ModelCode = "M-A", State = TradeItemState.Active });
            await db.SaveChangesAsync();
        }
        var store = new AuthoringCatalogueStore(factory);
        var absorber = new VariantNamingAbsorber(factory, store);

        await absorber.AbsorbAsync();

        var article = Assert.Single(await store.LoadArticlesAsync());
        Assert.Equal("FJCH", article.ElementCode);
        Assert.Equal(
            new Dictionary<string, string> { ["DEPTH"] = "STD", ["__MATERIAL__"] = "LEATHER-THICK" },
            article.Selections);
    }

    // A freshly-migrated DB always has LegacyVariantNamings (the rename runs unconditionally in Up()),
    // just empty - so the true "no staging table" case only exists after a first AbsorbAsync call has
    // already dropped it. The first call here exercises the empty-table path (renamed, no rows,
    // dropped); the second call is the actual idempotency assertion this test is named for.
    [Fact]
    public async Task Absorb_NoStagingTable_IsANoOp()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = new AuthoringCatalogueStore(factory);
        var absorber = new VariantNamingAbsorber(factory, store);
        await absorber.AbsorbAsync();

        await absorber.AbsorbAsync();

        Assert.Empty(await store.LoadArticlesAsync());
    }
}
