using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Configurator;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using Xunit;

namespace CheapFurniturePlanner.Tests.Configurator;

// Exercises the pure ConfigurationResolver helpers against the real embedded Fjord seed (the same
// fixture CatalogueEndToEndTests uses) so visibility/default behaviour is proven against actual
// catalogue data, not a hand-rolled stub that could drift from the real option shapes.
public class ConfigurationResolverTests
{
    private static CatalogueSnapshot LoadFjordSnapshot()
    {
        var asm = typeof(CataloguePublishService).Assembly;
        using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json")
            ?? throw new InvalidOperationException("Embedded resource 'CheapFurniturePlanner.Seed.demo-catalogue.json' not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return CanonicalJson.Deserialize<CatalogueSnapshot>(json)
            ?? throw new InvalidOperationException("Failed to deserialize embedded demo-catalogue.json.");
    }

    private static Domain.Catalog.Element FindFj2(CatalogueSnapshot snapshot) =>
        snapshot.Models.SelectMany(m => m.Elements).Single(e => e.Code == "FJ2");

    [Fact]
    public void DefaultSelections_ReturnsEachVisibleRequiredChoiceDefault_AndPricesWithoutIncompleteConfigurationError()
    {
        var snapshot = LoadFjordSnapshot();
        var element = FindFj2(snapshot);

        var selections = ConfigurationResolver.DefaultSelections(element);

        // HEAD is gated behind MECH=REC, which is not the default (MECH defaults to NONE), so it
        // must not appear in the default selection set.
        Assert.Equal("STD", selections["DEPTH"]);
        Assert.Equal("NONE", selections["MECH"]);
        Assert.Equal("PLAIN", selections["STITCH"]);
        Assert.False(selections.ContainsKey("HEAD"));

        var fabricColorCode = ConfigurationResolver.DefaultFabricColorCode(element, snapshot);
        Assert.NotNull(fabricColorCode);

        var config = new ProductConfiguration("FJORD",
            [new ElementSelection("FJ2", 1, selections, fabricColorCode)]);
        var market = snapshot.Markets[0];
        var result = PricingEngine.Calculate(new PricingRequest(snapshot, config, new PricingContext(market)));

        Assert.DoesNotContain(result.Errors, e => e.Kind == PricingErrorKind.IncompleteConfiguration);
    }

    [Fact]
    public void VisibleOptions_HidesOptionUntilItsTriggerIsSelected()
    {
        var snapshot = LoadFjordSnapshot();
        var element = FindFj2(snapshot);

        var withoutRec = ConfigurationResolver.VisibleOptions(element, new Dictionary<string, string> { ["MECH"] = "NONE" });
        Assert.DoesNotContain(withoutRec, o => o.OptionDefinitionCode == "HEAD");

        var withRec = ConfigurationResolver.VisibleOptions(element, new Dictionary<string, string> { ["MECH"] = "REC" });
        Assert.Contains(withRec, o => o.OptionDefinitionCode == "HEAD");
    }

    [Fact]
    public void FindModelCode_ReturnsOwningModelCode()
    {
        var snapshot = LoadFjordSnapshot();

        var modelCode = ConfigurationResolver.FindModelCode(snapshot, "FJ2");

        Assert.Equal("FJORD", modelCode);
    }

    [Fact]
    public void FindModelCode_ReturnsNull_ForUnknownElementCode()
    {
        var snapshot = LoadFjordSnapshot();

        var modelCode = ConfigurationResolver.FindModelCode(snapshot, "DOES-NOT-EXIST");

        Assert.Null(modelCode);
    }
}
