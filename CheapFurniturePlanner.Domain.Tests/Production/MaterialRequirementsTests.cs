using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Production;

// MaterialRequirements rides ResolveStage (visibility, applicability conditions, substitution rules)
// so forecast/backflush material lists can never disagree with the pricing engine's own cost lines.
// Combos below are the same FJORD/FJ2 selections the golden-master cases (std-plain-aqua-euw,
// deep-conditioned-foam, rec-substitution-head) already exercise.
public class MaterialRequirementsTests
{
    private static readonly Dictionary<string, string> StdSelections = new() { ["DEPTH"] = "STD", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" };

    [Fact]
    public void Resolve_MatchesEngineEffectiveLines()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var market = snapshot.Markets.Single(m => m.Code == "EUW");
        var configuration = new ProductConfiguration("FJORD", [new ElementSelection("FJ2", 1, StdSelections, "AQUA-BLUE")]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(market));

        // Act
        var needLines = MaterialRequirements.Resolve(snapshot, "FJORD", "FJ2", StdSelections, "AQUA-BLUE");
        var pricingResult = PricingEngine.Calculate(request);

        // Assert
        Assert.True(pricingResult.IsSuccess, $"Expected success but got: {string.Join(", ", pricingResult.Errors.Select(e => $"{e.Kind}:{e.Subject}"))}");
        var engineElement = Assert.Single(pricingResult.Breakdown!.Elements);

        Dictionary<string, MaterialKind> materialCategoryToKind = new()
        {
            ["frame"] = MaterialKind.Frame,
            ["foam"] = MaterialKind.Foam,
            ["cotton"] = MaterialKind.Cotton,
            ["misc"] = MaterialKind.Misc,
        };

        // Every foam/frame/cotton/misc BreakdownLine has a matching MaterialNeedLine with the same
        // code (parsed from the engine's "{Prefix} {Code}" description) and the same quantity - BOM
        // Quantity for foam/frame/misc, Measurement for cotton (both surface as BreakdownLine.Quantity).
        foreach (var line in engineElement.Lines.Where(l => materialCategoryToKind.ContainsKey(l.Category)))
        {
            var expectedCode = line.Description.Split(' ', 2)[1];
            var kind = materialCategoryToKind[line.Category];
            Assert.Contains(needLines, n => n.Kind == kind && n.Code == expectedCode && n.Quantity == line.Quantity);
        }

        // Fabric: the CutSort metrage plus the config's fabric colour code (not the price group code
        // the engine's own description uses).
        var fabricLine = engineElement.Lines.Single(l => l.Category == "fabric");
        Assert.Contains(needLines, n =>
            n.Kind == MaterialKind.Fabric && n.Code == "AQUA-BLUE" && n.FabricColorCode == "AQUA-BLUE" && n.Quantity == fabricLine.Quantity);

        // No extras beyond frame + foam + cotton + fabric + misc.
        Assert.Equal(5, needLines.Count);
    }

    [Fact]
    public void Resolve_RespectsConditionsAndSubstitutions()
    {
        // Arrange
        var snapshot = DemoWorld.Load();

        // deep-conditioned-foam: DEPTH=DEEP satisfies an ApplicabilityCondition that adds an extra
        // conditioned foam line alongside the base one.
        var deepSelections = new Dictionary<string, string> { ["DEPTH"] = "DEEP", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" };

        // rec-substitution-head: MECH=REC + HEAD=HS1 satisfies a SubstitutionRule that swaps the base
        // foam's material code (same BOM slot, same quantity - no QuantityOverride in this rule).
        var recSelections = new Dictionary<string, string> { ["DEPTH"] = "STD", ["MECH"] = "REC", ["HEAD"] = "HS1", ["STITCH"] = "PLAIN" };

        // Act
        var deepLines = MaterialRequirements.Resolve(snapshot, "FJORD", "FJ2", deepSelections, "AQUA-BLUE");
        var recLines = MaterialRequirements.Resolve(snapshot, "FJORD", "FJ2", recSelections, "AQUA-BLUE");

        // Assert
        Assert.Contains(deepLines, n => n.Kind == MaterialKind.Foam && n.Code == "FM-STD" && n.Quantity == 2m);
        Assert.Contains(deepLines, n => n.Kind == MaterialKind.Foam && n.Code == "FM-DEEP-PAD" && n.Quantity == 1m);

        Assert.DoesNotContain(recLines, n => n.Kind == MaterialKind.Foam && n.Code == "FM-STD");
        Assert.Contains(recLines, n => n.Kind == MaterialKind.Foam && n.Code == "FM-FIRM" && n.Quantity == 2m);
    }

    [Fact]
    public void Resolve_SkipsLaborAndSurcharges()
    {
        // Arrange
        var snapshot = DemoWorld.Load();

        // Act
        var needLines = MaterialRequirements.Resolve(snapshot, "FJORD", "FJ2", StdSelections, "AQUA-BLUE");

        // Assert: frame + foam + cotton + fabric + misc only - the element also carries OP-CUT/OP-SEW
        // labor lines and frame-assembly/spray surcharge lines that must never surface as materials.
        Assert.Equal(5, needLines.Count);
        Assert.DoesNotContain(needLines, n => n.Code is "OP-CUT" or "OP-SEW");
    }

    [Fact]
    public void Resolve_UnknownElementThrows()
    {
        // Arrange
        var snapshot = DemoWorld.Load();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MaterialRequirements.Resolve(snapshot, "FJORD", "DOES-NOT-EXIST", new Dictionary<string, string>(), null));
        Assert.Contains("DOES-NOT-EXIST", ex.Message);
    }
}
