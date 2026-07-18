using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// ModelAuthoringService is the studio's model lifecycle manager: create blank/clone, rename, and
// delete authoring documents. Rename/delete interact with ModelPublishService so an Active model's
// name change republishes the live catalogue, and an Active model cannot be deleted outright.
public class ModelAuthoringServiceTests
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

    private sealed record Harness(ModelAuthoringService Authoring, AuthoringCatalogueStore Store, ModelPublishService Publish, DbCatalogueSource Source, ArticleAuthoringService Articles);

    // RepublishAsync/GetAuthoringModelsAsync read the authoring store rather than the embedded seed
    // directly, so the store must be seeded from that same embedded seed for the harness to have
    // anything to work with.
    private static async Task<Harness> NewHarnessAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        var articles = new ArticleAuthoringService(store, publish);
        var authoring = new ModelAuthoringService(factory, store, publish, articles);
        return new Harness(authoring, store, publish, source, articles);
    }

    [Fact]
    public async Task CreateBlankAsync_AddsEmptyModel()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await harness.Authoring.CreateBlankAsync("NEWM", "New Model", null, null);

        Assert.Contains("NEWM", await harness.Store.ModelCodesAsync());
        var model = await harness.Store.LoadModelAsync("NEWM");
        Assert.NotNull(model);
        Assert.Empty(model!.Elements);
    }

    [Fact]
    public async Task CreateBlankAsync_DuplicateCode_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await harness.Authoring.CreateBlankAsync("NEWM", "New Model", null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Authoring.CreateBlankAsync("NEWM", "Another", null, null));
    }

    [Fact]
    public async Task CreateBlank_PersistsModelTypeCode()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await harness.Authoring.CreateBlankAsync("NEWM", "New Model", null, "MT-RELAX");

        var model = await harness.Store.LoadModelAsync("NEWM");
        Assert.NotNull(model);
        Assert.Equal("MT-RELAX", model!.ModelTypeCode);
    }

    [Fact]
    public async Task CreateFromCloneAsync_ClonesElements_LeavesSourceUnchanged()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        var source = await harness.Store.LoadModelAsync(Fjord);

        await harness.Authoring.CreateFromCloneAsync(Fjord, "FJORD-COPY", "Fjord Copy");

        var clone = await harness.Store.LoadModelAsync("FJORD-COPY");
        Assert.NotNull(clone);
        Assert.Equal("FJORD-COPY", clone!.Code);
        Assert.Equal("Fjord Copy", clone.Name);
        Assert.Equal(CanonicalJson.Serialize(source!.Elements), CanonicalJson.Serialize(clone.Elements));

        var reloadedSource = await harness.Store.LoadModelAsync(Fjord);
        Assert.Equal(CanonicalJson.Serialize(source), CanonicalJson.Serialize(reloadedSource));
    }

    [Fact]
    public async Task CreateFromCloneAsync_MissingSource_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Authoring.CreateFromCloneAsync("GHOST", "GHOST-COPY", "Ghost Copy"));
    }

    [Fact]
    public async Task RenameAsync_DraftModel_UpdatesName()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await harness.Authoring.RenameAsync(Studio, "Renamed", null, null);

        var model = await harness.Store.LoadModelAsync(Studio);
        Assert.Equal("Renamed", model!.Name);
    }

    [Fact]
    public async Task RenameAsync_ActiveModel_Republishes()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        await harness.Publish.SetStateAsync(Fjord, TradeItemState.Active);

        await harness.Authoring.RenameAsync(Fjord, "Fjord Renamed", null, null);

        var snapshot = await harness.Source.GetCurrentAsync();
        var published = Assert.Single(snapshot.Models, m => m.Code == Fjord);
        Assert.Equal("Fjord Renamed", published.Name);
    }

    [Fact]
    public async Task RenameAsync_ActiveModelRepublishFails_RevertsNameInStore()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        await harness.Publish.SetStateAsync(Fjord, TradeItemState.Active);
        var original = await harness.Store.LoadModelAsync(Fjord);
        var originalName = original!.Name;

        // Strip the elements out from under the still-Active model so the rename's republish fails
        // validation (a zero-element model can't be published), forcing the compensating revert.
        original.Elements.Clear();
        await harness.Store.SaveModelAsync(original);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Authoring.RenameAsync(Fjord, "Fjord Renamed", null, null));

        var reverted = await harness.Store.LoadModelAsync(Fjord);
        Assert.Equal(originalName, reverted!.Name);
    }

    [Fact]
    public async Task Rename_UpdatesModelTypeCode()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);

        await harness.Authoring.RenameAsync(Studio, "Renamed", null, "MT-RELAX");

        var withType = await harness.Store.LoadModelAsync(Studio);
        Assert.Equal("MT-RELAX", withType!.ModelTypeCode);

        await harness.Authoring.RenameAsync(Studio, "Renamed", null, null);

        var cleared = await harness.Store.LoadModelAsync(Studio);
        Assert.Null(cleared!.ModelTypeCode);
    }

    [Fact]
    public async Task DeleteAsync_ActiveModel_ThrowsModelActiveException()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        await harness.Publish.SetStateAsync(Fjord, TradeItemState.Active);

        await Assert.ThrowsAsync<ModelActiveException>(() => harness.Authoring.DeleteAsync(Fjord));
    }

    [Fact]
    public async Task DeleteAsync_NonActiveModel_RemovesDocAndStateAndNaming()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var harness = await NewHarnessAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
            await db.SaveChangesAsync();
        }
        await harness.Store.SaveArticlesAsync([new Article { Id = 1, AssignedCode = "STUDIO-A", ModelCode = Studio, ElementCode = "FS2", VariantCode = "V1" }]);

        await harness.Authoring.DeleteAsync(Studio);

        Assert.DoesNotContain(Studio, await harness.Store.ModelCodesAsync());
        await using var verify = factory.CreateDbContext();
        Assert.False(await verify.ModelStates.AnyAsync(s => s.ModelCode == Studio));
        Assert.DoesNotContain(await harness.Store.LoadArticlesAsync(), a => a.ModelCode == Studio);
    }
}
