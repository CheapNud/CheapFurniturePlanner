using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// The modellenkamer's naming registry: sparse (only named variants get a row) and gated by the
// model's publish state (Draft-only). Exercised over in-memory SQLite with the real
// VariantNamingService + ModelPublishService, following the ModelPublishGateTests harness pattern.
public class VariantNamingServiceTests
{
    private const string Studio = "FJORD-STUDIO";
    private const string Variant = "FJ2-__MATERIAL__:Fabric";

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

    private sealed record Harness(VariantNamingService Naming, ModelPublishService Publish);

    private static Harness NewHarness(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source);
        return new Harness(new VariantNamingService(factory, publish), publish);
    }

    // Seeds the two demo model states exactly as Program.cs does on first run: FJORD Active,
    // FJORD-STUDIO Draft.
    private static async Task SeedModelStatesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.ModelStates.Add(new ModelStateRecord { ModelCode = "FJORD", State = TradeItemState.Active });
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AssignAsync_WhileDraft_PersistsSparseRow()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = NewHarness(factory);

        await harness.Naming.AssignAsync(Studio, Variant, "18X");

        var names = await harness.Naming.NamesForModelAsync(Studio);
        Assert.Equal("18X", names[Variant]);
    }

    [Fact]
    public async Task AssignAsync_ReAssign_UpdatesCode()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = NewHarness(factory);

        await harness.Naming.AssignAsync(Studio, Variant, "18X");
        await harness.Naming.AssignAsync(Studio, Variant, "STUDIO-A");

        var names = await harness.Naming.NamesForModelAsync(Studio);
        Assert.Equal("STUDIO-A", names[Variant]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AssignAsync_BlankCode_UnnamesVariant(string blank)
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = NewHarness(factory);

        await harness.Naming.AssignAsync(Studio, Variant, "18X");
        await harness.Naming.AssignAsync(Studio, Variant, blank);

        var names = await harness.Naming.NamesForModelAsync(Studio);
        Assert.DoesNotContain(Variant, names.Keys);
    }

    [Fact]
    public async Task AssignAsync_AfterRelease_ThrowsNamingFrozenException()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = NewHarness(factory);

        await harness.Publish.ReleaseAsync(Studio);

        await Assert.ThrowsAsync<NamingFrozenException>(() => harness.Naming.AssignAsync(Studio, Variant, "18X"));
    }

    [Fact]
    public async Task NamesForModelAsync_ReturnsOnlyRowsForThatModel()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = NewHarness(factory);

        await harness.Naming.AssignAsync(Studio, Variant, "18X");

        var fjordNames = await harness.Naming.NamesForModelAsync("FJORD");
        Assert.Empty(fjordNames);

        var studioNames = await harness.Naming.NamesForModelAsync(Studio);
        Assert.Single(studioNames);
    }
}
