using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// The publish-time gate: the studio's per-model state controls what the planner's published
// catalogue actually contains. Only Active (released) models are published; releasing a Draft makes
// it appear, discontinuing an Active model removes it. Exercised end to end over in-memory SQLite
// with the real ModelPublishService + CataloguePublishService + DbCatalogueSource.
public class ModelPublishGateTests
{
    private const string Fjord = "FJORD";
    private const string Studio = "FJORD-STUDIO";

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

    private sealed record Harness(ModelPublishService Publish, DbCatalogueSource Source);

    // RepublishAsync now reads the authoring store rather than the embedded seed directly, so the
    // store must be seeded from that same embedded seed for the harness to have any models to publish.
    private static async Task<Harness> NewHarnessAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        var source = new DbCatalogueSource(factory);
        return new Harness(new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store), source);
    }

    // Seeds the two demo model states (FJORD Active, FJORD-STUDIO Draft) exactly as Program.cs does
    // on first run, then publishes the Active-only subset so the source has a current catalogue.
    private static async Task SeedActiveFjordAsync(IDbContextFactory<FurniturePlannerContext> factory, Harness harness)
    {
        await using (var db = factory.CreateDbContext())
        {
            db.ModelStates.Add(new ModelStateRecord { ModelCode = Fjord, State = TradeItemState.Active });
            db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
            await db.SaveChangesAsync();
        }
        await harness.Publish.RepublishAsync();
    }

    private static async Task<HashSet<string>> CurrentModelCodesAsync(DbCatalogueSource source)
    {
        var snapshot = await source.GetCurrentAsync();
        return snapshot.Models.Select(m => m.Code).ToHashSet();
    }

    [Fact]
    public async Task Republish_PublishesOnlyActiveModels()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await SeedActiveFjordAsync(factory, harness);

        var codes = await CurrentModelCodesAsync(harness.Source);
        Assert.Contains(Fjord, codes);
        Assert.DoesNotContain(Studio, codes);
    }

    [Fact]
    public async Task ReleaseAsync_MakesModelAppearInCurrentSnapshot()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await SeedActiveFjordAsync(factory, harness);
        Assert.DoesNotContain(Studio, await CurrentModelCodesAsync(harness.Source));

        await harness.Publish.ReleaseAsync(Studio);

        var codes = await CurrentModelCodesAsync(harness.Source);
        Assert.Contains(Studio, codes);
        Assert.Contains(Fjord, codes);
    }

    [Fact]
    public async Task DiscontinueAsync_RemovesModelFromCurrentSnapshot()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await SeedActiveFjordAsync(factory, harness);
        Assert.Contains(Fjord, await CurrentModelCodesAsync(harness.Source));

        await harness.Publish.DiscontinueAsync(Fjord);

        Assert.DoesNotContain(Fjord, await CurrentModelCodesAsync(harness.Source));
    }

    [Fact]
    public async Task Republish_EmptyActiveSet_PublishesZeroModelsAndIsValid()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        // No Active state rows at all -> the Active-only snapshot is empty, which is a valid publish.
        await harness.Publish.RepublishAsync();

        var snapshot = await harness.Source.GetCurrentAsync();
        Assert.Empty(snapshot.Models);
    }
}
