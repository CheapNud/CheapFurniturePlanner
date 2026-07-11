using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Draft-only authoring of an element's option list over the real AuthoringCatalogueStore +
// ModelPublishService on in-memory SQLite. Operates on seed Draft model FJORD-STUDIO, element FS2.
public class OptionAuthoringServiceTests
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

    private sealed record Harness(OptionAuthoringService Options, ModelPublishService Publish, VariantNamingService Naming, AuthoringCatalogueStore Store);

    private static async Task<Harness> NewHarnessAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        return new Harness(new OptionAuthoringService(factory, store, publish), publish, new VariantNamingService(factory, publish), store);
    }

    private static async Task SeedModelStatesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Active, State = TradeItemState.Active });
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
        await db.SaveChangesAsync();
    }

    private static ChoiceOption Choice(string defCode, bool affectsBom, params string[] valueCodes) => new()
    {
        OptionDefinitionCode = defCode,
        AffectsBom = affectsBom,
        Values = valueCodes.Select((vc, i) => new ProductOptionValue { OptionChoiceCode = vc, DisplayIndex = i, IsDefault = i == 0 }).ToList()
    };

    private static async Task<ProductOption> OptionOnAsync(Harness harness, string defCode)
        => (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == Element).Options.Single(o => o.OptionDefinitionCode == defCode);

    private static async Task NameAVariantAsync(Harness harness)
        => await harness.Naming.AssignAsync(Studio, "FS2-DEPTH:STD", "STUDIO-A");

    [Fact]
    public async Task AddChoiceOption_Appends()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Options.AddOptionAsync(Studio, Element, Choice("ARMS", affectsBom: true, "NONE", "ARM1"));

        var added = (ChoiceOption)await OptionOnAsync(harness, "ARMS");
        Assert.Equal(["NONE", "ARM1"], added.Values.Select(v => v.OptionChoiceCode));
    }

    [Fact]
    public async Task AddFabricOption_Appends()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Options.AddOptionAsync(Studio, Element, new FabricOption { OptionDefinitionCode = "PIPING", FabricGroupCodes = ["AQUA", "TERRA"] });

        var added = (FabricOption)await OptionOnAsync(harness, "PIPING");
        Assert.Equal(["AQUA", "TERRA"], added.FabricGroupCodes);
    }

    [Fact]
    public async Task AddOption_DuplicateDefCode_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddOptionAsync(Studio, Element, Choice("DEPTH", true, "X")));  // DEPTH already exists
    }

    [Theory]
    [InlineData("BAD-CODE")]
    [InlineData("BAD:CODE")]
    public async Task AddOption_DefCodeWithDelimiter_Throws(string defCode)
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddOptionAsync(Studio, Element, Choice(defCode, true, "X")));
    }

    [Theory]
    [InlineData("BAD-VAL")]
    [InlineData("BAD:VAL")]
    public async Task AddChoiceOption_ChoiceCodeWithDelimiter_Throws(string choiceCode)
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddOptionAsync(Studio, Element, Choice("ARMS", true, choiceCode)));
    }

    [Fact]
    public async Task AddChoiceOption_NoValues_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddOptionAsync(Studio, Element, new ChoiceOption { OptionDefinitionCode = "EMPTY", Values = [] }));
    }

    [Fact]
    public async Task AddChoiceOption_TwoDefaults_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        var twoDefaults = new ChoiceOption
        {
            OptionDefinitionCode = "ARMS",
            Values = [ new() { OptionChoiceCode = "A", IsDefault = true }, new() { OptionChoiceCode = "B", IsDefault = true } ]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddOptionAsync(Studio, Element, twoDefaults));
    }

    [Fact]
    public async Task AddOption_ReservedMaterialDefCode_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddOptionAsync(Studio, Element, Choice("__MATERIAL__", true, "X")));
    }

    [Fact]
    public async Task AddFabricOption_UnknownFabricGroup_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddOptionAsync(Studio, Element, new FabricOption { OptionDefinitionCode = "PIPING", FabricGroupCodes = ["BOGUS"] }));
    }

    [Fact]
    public async Task UpdateOption_PreservesVisibilityRulesAndDisplayIndex()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        var before = (ChoiceOption)await OptionOnAsync(harness, "HEAD");   // HEAD carries 1 VisibilityRule
        Assert.Single(before.VisibilityRules);
        var beforeIndex = before.DisplayIndex;

        // Replace HEAD's values wholesale (still BOM-significant).
        await harness.Options.UpdateOptionAsync(Studio, Element, "HEAD", Choice("HEAD", affectsBom: true, "HS1", "HS2", "HS3"));

        var after = (ChoiceOption)await OptionOnAsync(harness, "HEAD");
        Assert.Equal(["HS1", "HS2", "HS3"], after.Values.Select(v => v.OptionChoiceCode));
        Assert.Single(after.VisibilityRules);       // preserved
        Assert.Equal(beforeIndex, after.DisplayIndex);
    }

    [Fact]
    public async Task RemoveOption_Removes_AndRenumbers()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Options.RemoveOptionAsync(Studio, Element, "STITCH");

        var element = (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == Element);
        Assert.DoesNotContain(element.Options, o => o.OptionDefinitionCode == "STITCH");
        Assert.Equal(Enumerable.Range(0, element.Options.Count), element.Options.Select(o => o.DisplayIndex));
    }

    [Fact]
    public async Task ReorderOptions_ReordersAndRenumbers()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        var codes = (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == Element).Options.Select(o => o.OptionDefinitionCode).ToList();
        var reversed = Enumerable.Reverse(codes).ToList();

        await harness.Options.ReorderOptionsAsync(Studio, Element, reversed);

        var element = (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == Element);
        Assert.Equal(reversed, element.Options.Select(o => o.OptionDefinitionCode));
        Assert.Equal(Enumerable.Range(0, element.Options.Count), element.Options.Select(o => o.DisplayIndex));
    }

    [Fact]
    public async Task ReorderOptions_NonPermutation_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.ReorderOptionsAsync(Studio, Element, ["DEPTH", "DEPTH"]));
    }

    [Fact]
    public async Task AllMutations_OnActiveModel_ThrowStructureFrozen()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Options.AddOptionAsync(Active, "FJ2", Choice("ARMS", true, "X")));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Options.UpdateOptionAsync(Active, "FJ2", "DEPTH", Choice("DEPTH", true, "STD")));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Options.RemoveOptionAsync(Active, "FJ2", "DEPTH"));
        await Assert.ThrowsAsync<StructureFrozenException>(() => harness.Options.ReorderOptionsAsync(Active, "FJ2", ["DEPTH"]));
    }

    // --- prune anchors ---

    [Fact]
    public async Task AddBomSignificantOption_PrunesNamingRows()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await NameAVariantAsync(harness);
        Assert.Single(await harness.Naming.NamesForModelAsync(Studio));

        await harness.Options.AddOptionAsync(Studio, Element, Choice("ARMS", affectsBom: true, "NONE", "ARM1"));

        Assert.Empty(await harness.Naming.NamesForModelAsync(Studio));
    }

    [Fact]
    public async Task RemoveBomSignificantOption_PrunesNamingRows()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await NameAVariantAsync(harness);

        await harness.Options.RemoveOptionAsync(Studio, Element, "DEPTH");  // AffectsBom

        Assert.Empty(await harness.Naming.NamesForModelAsync(Studio));
    }

    [Fact]
    public async Task ToggleAffectsBomOff_ViaUpdate_PrunesNamingRows()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await NameAVariantAsync(harness);

        await harness.Options.UpdateOptionAsync(Studio, Element, "DEPTH", Choice("DEPTH", affectsBom: false, "STD", "DEEP"));

        Assert.Empty(await harness.Naming.NamesForModelAsync(Studio));
    }

    [Fact]
    public async Task ReorderOptions_DoesNotPrune()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await NameAVariantAsync(harness);
        var codes = (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == Element).Options.Select(o => o.OptionDefinitionCode).ToList();

        await harness.Options.ReorderOptionsAsync(Studio, Element, Enumerable.Reverse(codes).ToList());

        Assert.Single(await harness.Naming.NamesForModelAsync(Studio));   // untouched
    }

    [Fact]
    public async Task UpdateOption_DefCodeRenameOfBomOption_PrunesNamingRows()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await NameAVariantAsync(harness);

        await harness.Options.UpdateOptionAsync(Studio, Element, "DEPTH", Choice("DEPTH2", affectsBom: true, "STD", "DEEP"));

        Assert.Empty(await harness.Naming.NamesForModelAsync(Studio));
    }

    [Fact]
    public async Task UpdateOption_ValueCodeSetChangeOfBomOption_PrunesNamingRows()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await NameAVariantAsync(harness);

        await harness.Options.UpdateOptionAsync(Studio, Element, "DEPTH", Choice("DEPTH", affectsBom: true, "STD", "XDEEP"));

        Assert.Empty(await harness.Naming.NamesForModelAsync(Studio));
    }

    [Fact]
    public async Task EditNonBomOption_DoesNotPrune()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await NameAVariantAsync(harness);

        // STITCH is AffectsBom=false; changing its values does not affect VariantCode.
        await harness.Options.UpdateOptionAsync(Studio, Element, "STITCH", Choice("STITCH", affectsBom: false, "PLAIN", "CONTRAST", "DOUBLE"));

        Assert.Single(await harness.Naming.NamesForModelAsync(Studio));   // untouched
    }

    // --- visibility rules ---

    private static async Task<IReadOnlyList<VisibilityRule>> RulesOnAsync(Harness harness, string optionDefCode)
        => (await OptionOnAsync(harness, optionDefCode)).VisibilityRules;

    [Fact]
    public async Task AddVisibilityRule_Adds()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Options.AddVisibilityRuleAsync(Studio, Element, "STITCH", "DEPTH", "DEEP");

        var rules = await RulesOnAsync(harness, "STITCH");
        Assert.Contains(rules, r => r.TriggerOptionDefinitionCode == "DEPTH" && r.TriggerChoiceCode == "DEEP" && r.RevealedOptionDefinitionCode == "STITCH");
    }

    [Fact]
    public async Task AddVisibilityRule_SelfTrigger_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddVisibilityRuleAsync(Studio, Element, "STITCH", "STITCH", "PLAIN"));
    }

    [Fact]
    public async Task AddVisibilityRule_TriggerIsFabric_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddVisibilityRuleAsync(Studio, Element, "STITCH", "FABRIC", "AQUA"));
    }

    [Fact]
    public async Task AddVisibilityRule_TriggerChoiceNotInValues_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddVisibilityRuleAsync(Studio, Element, "STITCH", "DEPTH", "BOGUS"));
    }

    [Fact]
    public async Task AddVisibilityRule_Duplicate_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        // HEAD already has MECH:REC in the seed.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Options.AddVisibilityRuleAsync(Studio, Element, "HEAD", "MECH", "REC"));
    }

    [Fact]
    public async Task AddVisibilityRule_OnActiveModel_ThrowsStructureFrozen()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);
        await Assert.ThrowsAsync<StructureFrozenException>(() =>
            harness.Options.AddVisibilityRuleAsync(Active, "FJ2", "HEAD", "MECH", "REC"));
    }

    [Fact]
    public async Task RemoveVisibilityRule_Removes()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Options.RemoveVisibilityRuleAsync(Studio, Element, "HEAD", "MECH", "REC");

        Assert.Empty(await RulesOnAsync(harness, "HEAD"));
    }

    [Fact]
    public async Task RenameTriggerOption_MigratesDependentRule()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Options.UpdateOptionAsync(Studio, Element, "MECH", Choice("MECH2", affectsBom: true, "NONE", "REC"));

        var rules = await RulesOnAsync(harness, "HEAD");
        Assert.Single(rules);
        Assert.Equal(new VisibilityRule("MECH2", "REC", "HEAD"), rules[0]);
    }

    [Fact]
    public async Task RemoveTriggerOption_DropsDependentRule()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Options.RemoveOptionAsync(Studio, Element, "MECH");

        Assert.Empty(await RulesOnAsync(harness, "HEAD"));
    }

    [Fact]
    public async Task RemoveTriggerChoiceValue_DropsDependentRule()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        // MECH keeps its code but drops the REC value -> HEAD's MECH:REC rule no longer resolves.
        await harness.Options.UpdateOptionAsync(Studio, Element, "MECH", Choice("MECH", affectsBom: true, "NONE"));

        Assert.Empty(await RulesOnAsync(harness, "HEAD"));
    }

    [Fact]
    public async Task RenameNonTriggerOption_KeepsDependentRule()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        // STITCH is not HEAD's trigger, so renaming it must not touch HEAD's rule.
        await harness.Options.UpdateOptionAsync(Studio, Element, "STITCH", Choice("STITCH2", affectsBom: false, "PLAIN", "CONTRAST"));

        var rules = await RulesOnAsync(harness, "HEAD");
        Assert.Single(rules);
        Assert.Equal(new VisibilityRule("MECH", "REC", "HEAD"), rules[0]);
    }

    // --- BOM condition cascade ---

    private static async Task<BomLine?> BomLineAsync(Harness harness, string lineKey)
        => (await harness.Store.LoadModelAsync(Studio))!.Elements.Single(e => e.Code == Element)
            .Bom.Sections.SelectMany(s => s.Lines).FirstOrDefault(l => l.LineKey == lineKey);

    [Fact]
    public async Task RenameOption_MigratesBomLineCondition()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        // DEPTH (STD/DEEP) is referenced by FM-DEEP-FS2's condition WHEN DEPTH=DEEP.
        await harness.Options.UpdateOptionAsync(Studio, Element, "DEPTH", Choice("DEPTH2", affectsBom: true, "STD", "DEEP"));

        var line = await BomLineAsync(harness, "FM-DEEP-FS2");
        Assert.Equal(new ApplicabilityCondition([new SelectionKey("DEPTH2", "DEEP")]), line!.Condition);
    }

    [Fact]
    public async Task RemoveOption_DropsConditionedBomLine()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Options.RemoveOptionAsync(Studio, Element, "DEPTH");

        Assert.Null(await BomLineAsync(harness, "FM-DEEP-FS2"));      // dropped (condition dangled)
        Assert.NotNull(await BomLineAsync(harness, "FM-BASE-FS2"));   // unconditional line survives
    }

    [Fact]
    public async Task RemoveChoiceValue_DropsConditionedBomLine()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        // DEPTH keeps its code but drops the DEEP value -> FM-DEEP-FS2's WHEN DEPTH=DEEP no longer resolves.
        await harness.Options.UpdateOptionAsync(Studio, Element, "DEPTH", Choice("DEPTH", affectsBom: true, "STD"));

        Assert.Null(await BomLineAsync(harness, "FM-DEEP-FS2"));
    }

    [Fact]
    public async Task RemoveNonReferencedOption_KeepsBomLines()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        await SeedModelStatesAsync(factory);
        var harness = await NewHarnessAsync(factory);

        await harness.Options.RemoveOptionAsync(Studio, Element, "STITCH");   // no BOM line references STITCH

        Assert.NotNull(await BomLineAsync(harness, "FM-DEEP-FS2"));
    }
}
