using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Draft-only authoring of an element's BOM structure over the real AuthoringCatalogueStore +
// ModelPublishService on in-memory SQLite. Operates on seed Draft model FJORD-STUDIO, element FS2.
public class BomAuthoringServiceTests
{
    private const string Studio = "FJORD-STUDIO";
    private const string Active = "FJORD";
    private const string Element = "FS2";

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

    private sealed record Harness(BomAuthoringService Bom, ModelPublishService Publish, ArticleAuthoringService Naming, AuthoringCatalogueStore Store);

    private static async Task<Harness> NewHarnessAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        var naming = new ArticleAuthoringService(store, publish);
        return new Harness(new BomAuthoringService(factory, store, publish), publish, naming, store);
    }

    private static async Task SeedModelStatesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Active, State = TradeItemState.Active });
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
        await db.SaveChangesAsync();
    }

    private static async Task<BomDocument> BomAsync(Harness harness)
        => (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == Element).Bom;

    private static async Task<BomLine?> LineAsync(Harness harness, string lineKey)
        => (await BomAsync(harness)).Sections.SelectMany(s => s.Lines).FirstOrDefault(l => l.LineKey == lineKey);

    [Fact]
    public async Task AddLine_EachKind_AppendsToItsSection()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Frame, new FrameBomLine { LineKey = "FR-NEW", FrameBodyCode = "FBX", Colored = false });
        await harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Foam, new FoamBomLine { LineKey = "FO-NEW", FoamCode = "FM-STD" });
        await harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Cotton, new CottonBomLine { LineKey = "CT-NEW", CottonQualityCode = "COT-STD", Measurement = 1, CutUnits = 1 });
        await harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.CutSort, new CutSortBomLine { LineKey = "CS-NEW", Metrage = 2, CutUnits = 1, SecondaryGroupMetrages = new() { ["AQUA"] = 0.5m } });
        await harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Misc, new MiscBomLine { LineKey = "MI-NEW", MaterialCode = "GLUE" });
        await harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Labor, new LaborBomLine { LineKey = "LB-NEW", OperationCode = "OP-CUT", Units = 2 });

        var bom = await BomAsync(harness);
        Assert.Contains(bom.Sections.Single(s => s.Kind == BomSectionKind.Frame).Lines, l => l.LineKey == "FR-NEW");
        Assert.IsType<CutSortBomLine>(bom.Sections.Single(s => s.Kind == BomSectionKind.CutSort).Lines.Single(l => l.LineKey == "CS-NEW"));
        Assert.Contains(bom.Sections.Single(s => s.Kind == BomSectionKind.Labor).Lines, l => l.LineKey == "LB-NEW");
    }

    [Fact]
    public async Task AddLine_DuplicateLineKey_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Frame, new FrameBomLine { LineKey = "FR-FS2", FrameBodyCode = "FBX" }));  // FR-FS2 exists
    }

    [Fact]
    public async Task AddLine_BadFrameBody_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Frame, new FrameBomLine { LineKey = "FR-NEW", FrameBodyCode = "BOGUS" }));
    }

    [Fact]
    public async Task AddLine_BadMaterial_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Misc, new MiscBomLine { LineKey = "MI-NEW", MaterialCode = "BOGUS" }));
    }

    [Fact]
    public async Task AddLine_BadOperation_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Labor, new LaborBomLine { LineKey = "LB-NEW", OperationCode = "BOGUS", Units = 1 }));
    }

    [Fact]
    public async Task AddLine_BadCutSortGroup_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.CutSort, new CutSortBomLine { LineKey = "CS-NEW", Metrage = 1, CutUnits = 1, SecondaryGroupMetrages = new() { ["BOGUS"] = 1m } }));
    }

    [Fact]
    public async Task AddLine_KindMismatch_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Frame, new LaborBomLine { LineKey = "X", OperationCode = "OP-CUT", Units = 1 }));
    }

    [Fact]
    public async Task AddLine_NegativeQuantity_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Frame, new FrameBomLine { LineKey = "FR-NEG", FrameBodyCode = "FBX", Quantity = -1m }));
    }

    private static ApplicabilityCondition Cond(params (string Option, string Choice)[] keys)
        => new(keys.Select(k => new SelectionKey(k.Option, k.Choice)).ToList());

    [Fact]
    public async Task AddLine_WithValidCondition_StoresIt()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Misc,
            new MiscBomLine { LineKey = "MI-COND", MaterialCode = "GLUE", Condition = Cond(("DEPTH", "DEEP")) });

        var line = await LineAsync(harness, "MI-COND");
        Assert.Equal(Cond(("DEPTH", "DEEP")), line!.Condition);
    }

    [Fact]
    public async Task AddLine_ConditionUnknownOption_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Misc,
                new MiscBomLine { LineKey = "MI-COND", MaterialCode = "GLUE", Condition = Cond(("BOGUS", "X")) }));
    }

    [Fact]
    public async Task AddLine_ConditionUnknownChoice_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Misc,
                new MiscBomLine { LineKey = "MI-COND", MaterialCode = "GLUE", Condition = Cond(("DEPTH", "BOGUS")) }));
    }

    [Fact]
    public async Task AddLine_ConditionDuplicateOption_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Misc,
                new MiscBomLine { LineKey = "MI-COND", MaterialCode = "GLUE", Condition = Cond(("DEPTH", "DEEP"), ("DEPTH", "STD")) }));
    }

    [Fact]
    public async Task UpdateLine_SetsCondition()   // replaces the 9A UpdateLine_PreservesCondition test
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        // FM-DEEP-FS2 starts with WHEN DEPTH=DEEP; the dialog now owns the condition, so update sets it.
        await harness.Bom.UpdateLineAsync(Studio, Element, "FM-DEEP-FS2",
            new FoamBomLine { LineKey = "FM-DEEP-FS2", FoamCode = "FM-DEEP-PAD", Quantity = 1, Condition = Cond(("HEAD", "HS1")) });
        Assert.Equal(Cond(("HEAD", "HS1")), (await LineAsync(harness, "FM-DEEP-FS2"))!.Condition);

        // Updating with a null condition makes the line unconditional.
        await harness.Bom.UpdateLineAsync(Studio, Element, "FM-DEEP-FS2",
            new FoamBomLine { LineKey = "FM-DEEP-FS2", FoamCode = "FM-DEEP-PAD", Quantity = 1, Condition = null });
        Assert.Null((await LineAsync(harness, "FM-DEEP-FS2"))!.Condition);
    }

    // __MATERIAL__ is the synthetic selection ResolveStage injects from the resolved material type at
    // pricing time (VariantCode.MaterialDefCode) - it is never an authored ChoiceOption, so
    // ValidateCondition must carve it out rather than rejecting it as an unknown option. FSCH is the
    // seeded Draft element (FJORD-STUDIO) whose LB-LEATHERWORK-FSCH labor line already carries this
    // condition; keeping it on an update proves editing that line stays safe.
    [Fact]
    public async Task AddLine_WithMaterialCondition_DoesNotThrow()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Misc,
            new MiscBomLine { LineKey = "MI-LEATHER", MaterialCode = "GLUE", Condition = Cond(("__MATERIAL__", "LEATHER-THICK")) });

        var line = await LineAsync(harness, "MI-LEATHER");
        Assert.Equal(Cond(("__MATERIAL__", "LEATHER-THICK")), line!.Condition);
    }

    [Fact]
    public async Task UpdateLine_KeepingMaterialCondition_DoesNotThrow()
    {
        const string element = "FSCH";
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Bom.UpdateLineAsync(Studio, element, "LB-LEATHERWORK-FSCH",
            new LaborBomLine { LineKey = "LB-LEATHERWORK-FSCH", OperationCode = "OP-LEATHERWORK", Units = 3, Condition = Cond(("__MATERIAL__", "LEATHER-THICK")) });

        var bom = (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == element).Bom;
        var updated = bom.Sections.SelectMany(s => s.Lines).Single(l => l.LineKey == "LB-LEATHERWORK-FSCH");
        Assert.Equal(Cond(("__MATERIAL__", "LEATHER-THICK")), updated.Condition);
        Assert.Equal(3m, ((LaborBomLine)updated).Units);
    }

    [Fact]
    public async Task RemoveLine_DropsEmptiedSection()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Bom.RemoveLineAsync(Studio, Element, "FR-FS2");   // the only Frame line

        var bom = await BomAsync(harness);
        Assert.DoesNotContain(bom.Sections, s => s.Kind == BomSectionKind.Frame);
    }

    [Fact]
    public async Task ReorderLines_ReordersWithinSection()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Bom.ReorderLinesAsync(Studio, Element, BomSectionKind.Labor, ["LB-SEW-FS2", "LB-CUT-FS2"]);

        var labor = (await BomAsync(harness)).Sections.Single(s => s.Kind == BomSectionKind.Labor);
        Assert.Equal(["LB-SEW-FS2", "LB-CUT-FS2"], labor.Lines.Select(l => l.LineKey));
    }

    [Fact]
    public async Task ReorderLines_NonPermutation_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.ReorderLinesAsync(Studio, Element, BomSectionKind.Labor, ["LB-CUT-FS2", "LB-CUT-FS2"]));
    }

    [Fact]
    public async Task AllMutations_OnActiveModel_ThrowStructureFrozen()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Bom.AddLineAsync(Active, "FJ2", BomSectionKind.Frame, new FrameBomLine { LineKey = "X", FrameBodyCode = "FBX" }));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Bom.UpdateLineAsync(Active, "FJ2", "FR-FJ2", new FrameBomLine { LineKey = "FR-FJ2", FrameBodyCode = "FBX" }));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Bom.RemoveLineAsync(Active, "FJ2", "FR-FJ2"));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Bom.ReorderLinesAsync(Active, "FJ2", BomSectionKind.Frame, ["FR-FJ2"]));
    }

    [Fact]
    public async Task BomMutations_DoNotPruneNamingRows()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await harness.Naming.AssignAsync(Studio, Element, "FS2-DEPTH:STD", new Dictionary<string, string> { ["DEPTH"] = "STD" }, "STUDIO-A");
        Assert.Single(await harness.Naming.NamesForModelAsync(Studio));

        await harness.Bom.AddLineAsync(Studio, Element, BomSectionKind.Misc, new MiscBomLine { LineKey = "MI-NEW", MaterialCode = "GLUE" });
        await harness.Bom.UpdateLineAsync(Studio, Element, "MI-NEW", new MiscBomLine { LineKey = "MI-NEW", MaterialCode = "RESIN" });
        await harness.Bom.RemoveLineAsync(Studio, Element, "MI-NEW");

        Assert.Single(await harness.Naming.NamesForModelAsync(Studio));   // BOM authoring never touches VariantNaming
    }

    // --- substitution CRUD ---

    private static async Task<IReadOnlyList<SubstitutionRule>> SubsAsync(Harness harness)
        => (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == Element).Substitutions;

    private static SubstitutionRule Sub(ApplicabilityCondition when, string replace, string with, decimal? qty = null)
        => new(when, replace, with, qty);

    [Fact]
    public async Task AddSubstitution_Appends()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Bom.AddSubstitutionAsync(Studio, Element, Sub(Cond(("DEPTH", "DEEP")), "GLUE", "RESIN"));

        Assert.Contains(await SubsAsync(harness), s => s.ReplaceMaterialCode == "GLUE" && s.WithMaterialCode == "RESIN"
            && s.When.Equals(Cond(("DEPTH", "DEEP"))));
    }

    [Fact]
    public async Task UpdateSubstitution_ReplacesAtIndex()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);   // seed FS2 already has one substitution at index 0

        await harness.Bom.UpdateSubstitutionAsync(Studio, Element, 0, Sub(new ApplicabilityCondition([]), "GLUE", "RESIN", 2m));

        var subs = await SubsAsync(harness);
        Assert.Single(subs);
        Assert.Equal("GLUE", subs[0].ReplaceMaterialCode);
        Assert.Equal(2m, subs[0].QuantityOverride);
    }

    [Fact]
    public async Task RemoveSubstitution_RemovesAtIndex()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Bom.RemoveSubstitutionAsync(Studio, Element, 0);

        Assert.Empty(await SubsAsync(harness));
    }

    [Fact]
    public async Task RemoveSubstitution_OutOfRange_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Bom.RemoveSubstitutionAsync(Studio, Element, 9));
    }

    [Theory]
    [InlineData("BOGUS", "FM-FIRM")]
    [InlineData("FM-STD", "BOGUS")]
    public async Task AddSubstitution_BadMaterial_Throws(string replace, string with)
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddSubstitutionAsync(Studio, Element, Sub(new ApplicabilityCondition([]), replace, with)));
    }

    [Fact]
    public async Task AddSubstitution_UnknownConditionOption_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddSubstitutionAsync(Studio, Element, Sub(Cond(("BOGUS", "X")), "GLUE", "RESIN")));
    }

    [Fact]
    public async Task AddSubstitution_NegativeQuantityOverride_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Bom.AddSubstitutionAsync(Studio, Element, Sub(new ApplicabilityCondition([]), "GLUE", "RESIN", -1m)));
    }

    [Fact]
    public async Task SubstitutionMutations_OnActiveModel_ThrowStructureFrozen()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Bom.AddSubstitutionAsync(Active, "FJ2", Sub(new ApplicabilityCondition([]), "FM-STD", "FM-FIRM")));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Bom.UpdateSubstitutionAsync(Active, "FJ2", 0, Sub(new ApplicabilityCondition([]), "FM-STD", "FM-FIRM")));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Bom.RemoveSubstitutionAsync(Active, "FJ2", 0));
    }
}
