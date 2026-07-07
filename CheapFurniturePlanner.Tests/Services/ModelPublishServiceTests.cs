using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// ModelPublishService is the modellenkamer's gatekeeper: it lists the authoring model set (the
// embedded seed catalogue, in this slice) alongside each model's release state, and owns the
// Draft -> Active -> Discontinued state machine. Mirrors DbCatalogueSourceTests.NewFactory(): the
// connection is not owned by any single context handed out by the factory, so callers must dispose
// it themselves to keep the in-memory database alive for the test's duration.
public class ModelPublishServiceTests
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

    // A fully-wired service: the state machine now republishes the Active-only snapshot on every
    // transition, so it needs a real CataloguePublishService + ICatalogueSource over the same factory.
    private static ModelPublishService NewService(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var source = new DbCatalogueSource(factory);
        return new ModelPublishService(factory, new CataloguePublishService(factory, source), source);
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task GetStateAsync_UnseenModel_ReturnsDraft()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        var state = await service.GetStateAsync("FJORD");

        Assert.Equal(TradeItemState.Draft, state);
    }

    [Fact]
    public async Task ReleaseAsync_TransitionsDraftToActive_SecondReleaseThrows()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        await service.ReleaseAsync("FJORD");

        Assert.Equal(TradeItemState.Active, await service.GetStateAsync("FJORD"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReleaseAsync("FJORD"));
    }

    [Fact]
    public async Task DiscontinueAsync_RequiresActive_ThrowsWhileDraft()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DiscontinueAsync("FJORD"));
    }

    [Fact]
    public async Task DiscontinueAsync_AfterRelease_TransitionsActiveToDiscontinued()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        await service.ReleaseAsync("FJORD");
        await service.DiscontinueAsync("FJORD");

        Assert.Equal(TradeItemState.Discontinued, await service.GetStateAsync("FJORD"));
    }

    [Fact]
    public async Task GetAuthoringModelsAsync_ListsSeedModels_WithDefaultDraftState()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        var models = await service.GetAuthoringModelsAsync();

        var fjord = Assert.Single(models, m => m.Code == "FJORD");
        Assert.Equal("Fjord", fjord.Name);
        Assert.Equal(TradeItemState.Draft, fjord.State);
    }

    [Fact]
    public async Task GetAuthoringModelsAsync_ReflectsReleasedState()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        await service.ReleaseAsync("FJORD");
        var models = await service.GetAuthoringModelsAsync();

        var fjord = Assert.Single(models, m => m.Code == "FJORD");
        Assert.Equal(TradeItemState.Active, fjord.State);
    }
}
