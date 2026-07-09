using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// ModelPublishService is the studio's gatekeeper: it lists the authoring model set (the
// embedded seed catalogue, in this slice) alongside each model's release state. State transitions
// are free-flow - SetStateAsync can move a model to any state at any time - with one hard rule: a
// model with zero elements can't be published (set Active). Mirrors DbCatalogueSourceTests.NewFactory():
// the connection is not owned by any single context handed out by the factory, so callers must dispose
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
    // RepublishAsync/GetAuthoringModelsAsync now read the authoring store rather than the embedded
    // seed directly, so the store must be seeded from that same embedded seed first.
    private static async Task<ModelPublishService> NewServiceAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        var source = new DbCatalogueSource(factory);
        return new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
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
        var service = await NewServiceAsync(factory);

        var state = await service.GetStateAsync("FJORD");

        Assert.Equal(TradeItemState.Draft, state);
    }

    [Fact]
    public async Task SetStateAsync_ToActive_TransitionsDraftToActive_AndIsIdempotent()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = await NewServiceAsync(factory);

        await service.SetStateAsync("FJORD", TradeItemState.Active);

        Assert.Equal(TradeItemState.Active, await service.GetStateAsync("FJORD"));

        // Free-flow: setting a model to the state it is already in is not an error.
        await service.SetStateAsync("FJORD", TradeItemState.Active);
        Assert.Equal(TradeItemState.Active, await service.GetStateAsync("FJORD"));
    }

    [Fact]
    public async Task SetStateAsync_ToDiscontinued_FromDraft_Succeeds()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = await NewServiceAsync(factory);

        // Free-flow: there is no from-state gate, so a Draft model can move straight to Discontinued.
        await service.SetStateAsync("FJORD", TradeItemState.Discontinued);

        Assert.Equal(TradeItemState.Discontinued, await service.GetStateAsync("FJORD"));
    }

    [Fact]
    public async Task SetStateAsync_ToDiscontinued_AfterActive_TransitionsActiveToDiscontinued()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = await NewServiceAsync(factory);

        await service.SetStateAsync("FJORD", TradeItemState.Active);
        await service.SetStateAsync("FJORD", TradeItemState.Discontinued);

        Assert.Equal(TradeItemState.Discontinued, await service.GetStateAsync("FJORD"));
    }

    [Theory]
    [InlineData(TradeItemState.Draft)]
    [InlineData(TradeItemState.Active)]
    [InlineData(TradeItemState.Discontinued)]
    [InlineData(TradeItemState.PhasingOut)]
    public async Task SetStateAsync_AnyState_PersistsAndIsReadableBack(TradeItemState state)
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = await NewServiceAsync(factory);

        await service.SetStateAsync("FJORD", state);

        Assert.Equal(state, await service.GetStateAsync("FJORD"));
    }

    [Fact]
    public async Task SetStateAsync_ToActive_NoElementModel_ThrowsAndLeavesPriorState()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        await store.SaveModelAsync(new FurnitureModel { Code = "EMPTY", Name = "Empty", Elements = [] });
        var source = new DbCatalogueSource(factory);
        var service = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        await using (var db = factory.CreateDbContext())
        {
            db.ModelStates.Add(new ModelStateRecord { ModelCode = "EMPTY", State = TradeItemState.Draft });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetStateAsync("EMPTY", TradeItemState.Active));

        Assert.Equal(TradeItemState.Draft, await service.GetStateAsync("EMPTY"));
    }

    [Fact]
    public async Task GetAuthoringModelsAsync_ListsSeedModels_WithDefaultDraftState()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = await NewServiceAsync(factory);

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
        var service = await NewServiceAsync(factory);

        await service.SetStateAsync("FJORD", TradeItemState.Active);
        var models = await service.GetAuthoringModelsAsync();

        var fjord = Assert.Single(models, m => m.Code == "FJORD");
        Assert.Equal(TradeItemState.Active, fjord.State);
    }
}
