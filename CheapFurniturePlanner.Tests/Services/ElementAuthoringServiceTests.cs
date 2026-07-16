using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Exercises Draft-only element-list authoring over the real AuthoringCatalogueStore +
// ModelPublishService on in-memory SQLite, following the VariantNamingServiceTests harness.
public class ElementAuthoringServiceTests
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

    private sealed record Harness(ElementAuthoringService Elements, ModelPublishService Publish, ArticleAuthoringService Naming, AuthoringCatalogueStore Store);

    private static async Task<Harness> NewHarnessAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        var naming = new ArticleAuthoringService(store, publish);
        return new Harness(new ElementAuthoringService(factory, store, publish, naming), publish, naming, store);
    }

    private static async Task SeedModelStatesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Active, State = TradeItemState.Active });
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AddElementAsync_AppendsElementWithEmptyOptionsAndBom()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        var before = (await harness.Store.LoadModelAsync(Studio))!.Elements.Count;

        await harness.Elements.AddElementAsync(Studio, new Element { Code = "SEAT", Name = "Seat", Width = 80, Depth = 90, Height = 85, TransportUnits = 1 });

        var model = (await harness.Store.LoadModelAsync(Studio))!;
        var added = model.Elements.Single(e => e.Code == "SEAT");
        Assert.Equal(before + 1, model.Elements.Count);
        Assert.Empty(added.Options);
        Assert.Empty(added.Bom.Sections);
        Assert.Equal(model.Elements.Count - 1, added.DisplayIndex);
    }

    [Fact]
    public async Task AddElementAsync_DuplicateCode_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Elements.AddElementAsync(Studio, new Element { Code = "FS2", Name = "Dup" }));
    }

    [Fact]
    public async Task AddElementAsync_BlankName_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Elements.AddElementAsync(Studio, new Element { Code = "SEAT", Name = "   " }));
    }

    [Fact]
    public async Task AddElementAsync_CodeWithHyphen_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Elements.AddElementAsync(Studio, new Element { Code = "BAD-CODE", Name = "Bad" }));
    }

    [Fact]
    public async Task UpdateElementAsync_ChangesScalars_PreservesOptionsAndBom()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        var original = (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == "FS2");
        var originalOptionCount = original.Options.Count;
        var originalBomSectionCount = original.Bom.Sections.Count;
        var originalSubstitutionCount = original.Substitutions.Count;
        var originalDisplayIndex = original.DisplayIndex;

        await harness.Elements.UpdateElementAsync(Studio, "FS2", new Element { Code = "FS2", Name = "Renamed", Width = 111, Depth = 1, Height = 1, TransportUnits = 2 });

        var updated = (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == "FS2");
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(111, updated.Width);
        Assert.Equal(originalOptionCount, updated.Options.Count); // options/BOM untouched
        Assert.Equal(originalBomSectionCount, updated.Bom.Sections.Count);
        Assert.Equal(originalSubstitutionCount, updated.Substitutions.Count);
        Assert.Equal(originalDisplayIndex, updated.DisplayIndex);
    }

    [Fact]
    public async Task UpdateElementAsync_CodeChange_PrunesStrandedNamingRows()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        // Name a real variant of element FS2 while the model is Draft.
        await harness.Naming.AssignAsync(Studio, "FS2", "FS2-__MATERIAL__:Fabric", new Dictionary<string, string> { ["__MATERIAL__"] = "Fabric" }, "STUDIO-A");
        Assert.Single(await harness.Naming.NamesForModelAsync(Studio));

        await harness.Elements.UpdateElementAsync(Studio, "FS2", new Element { Code = "SEAT2", Name = "Seat 2" });

        var model = (await harness.Store.LoadModelAsync(Studio))!;
        Assert.Contains(model.Elements, e => e.Code == "SEAT2");
        Assert.DoesNotContain(model.Elements, e => e.Code == "FS2");
        Assert.Empty(await harness.Naming.NamesForModelAsync(Studio)); // stranded FS2-* row pruned
    }

    [Fact]
    public async Task RemoveElementAsync_PrunesNamingRows_AndRenumbers()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await harness.Naming.AssignAsync(Studio, "FS2", "FS2-__MATERIAL__:Fabric", new Dictionary<string, string> { ["__MATERIAL__"] = "Fabric" }, "STUDIO-A");

        await harness.Elements.RemoveElementAsync(Studio, "FS2");

        var model = (await harness.Store.LoadModelAsync(Studio))!;
        Assert.DoesNotContain(model.Elements, e => e.Code == "FS2");
        Assert.Empty(await harness.Naming.NamesForModelAsync(Studio));
        // DisplayIndex renumbered to contiguous 0..n-1
        Assert.Equal(Enumerable.Range(0, model.Elements.Count), model.Elements.Select(e => e.DisplayIndex));
    }

    [Fact]
    public async Task RemoveElementAsync_PrunesOnlyTargetElementNamingRows()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        // Name a real variant of FS2 and a real variant of FS3 while the model is Draft.
        await harness.Naming.AssignAsync(Studio, "FS2", "FS2-__MATERIAL__:Fabric", new Dictionary<string, string> { ["__MATERIAL__"] = "Fabric" }, "STUDIO-A");
        await harness.Naming.AssignAsync(Studio, "FS3", "FS3-__MATERIAL__:Fabric", new Dictionary<string, string> { ["__MATERIAL__"] = "Fabric" }, "STUDIO-B");
        Assert.Equal(2, (await harness.Naming.NamesForModelAsync(Studio)).Count);

        await harness.Elements.RemoveElementAsync(Studio, "FS2");

        var remaining = await harness.Naming.NamesForModelAsync(Studio);
        Assert.False(remaining.ContainsKey("FS2-__MATERIAL__:Fabric"));
        Assert.True(remaining.ContainsKey("FS3-__MATERIAL__:Fabric")); // sibling survives the prune
    }

    [Fact]
    public async Task ReorderElementsAsync_ReordersAndRenumbers()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await harness.Elements.AddElementAsync(Studio, new Element { Code = "SEAT", Name = "Seat" });
        var codes = (await harness.Store.LoadModelAsync(Studio))!.Elements.Select(e => e.Code).ToList();
        var reversed = Enumerable.Reverse(codes).ToList();

        await harness.Elements.ReorderElementsAsync(Studio, reversed);

        var model = (await harness.Store.LoadModelAsync(Studio))!;
        Assert.Equal(reversed, model.Elements.Select(e => e.Code).ToList());
        Assert.Equal(Enumerable.Range(0, model.Elements.Count), model.Elements.Select(e => e.DisplayIndex));
    }

    [Fact]
    public async Task ReorderElementsAsync_NonPermutation_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        // Count-mismatch branch.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Elements.ReorderElementsAsync(Studio, ["FS2", "FS2"]));

        // Same-count-but-duplicate branch (trips HashSet.SetEquals, not the count check).
        var realCodes = (await harness.Store.LoadModelAsync(Studio))!.Elements.Select(e => e.Code).ToList();
        var duplicatedCodes = realCodes.ToList();
        duplicatedCodes[^1] = duplicatedCodes[0];
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Elements.ReorderElementsAsync(Studio, duplicatedCodes));
    }

    [Fact]
    public async Task AllMutations_OnActiveModel_ThrowStructureFrozen()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        // FJORD is Active in the seed and has elements, so it stays Active.

        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Elements.AddElementAsync(Active, new Element { Code = "X", Name = "X" }));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Elements.UpdateElementAsync(Active, "FJ2", new Element { Code = "FJ2", Name = "X" }));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Elements.RemoveElementAsync(Active, "FJ2"));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Elements.ReorderElementsAsync(Active, ["FJ2"]));
    }
}
