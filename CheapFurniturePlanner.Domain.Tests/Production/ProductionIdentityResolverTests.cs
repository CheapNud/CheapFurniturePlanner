using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Production;

// ProductionIdentityResolver turns a configuration into per-element production identities, applying the
// three-state rule: no suggestion => Composed; a suggestion under a Draft model => Provisional; a
// suggestion under Active/Discontinued => Released (only Active is exportable).
public class ProductionIdentityResolverTests
{
    private static ElementSelection FjchSelection(string fabricColorCode) =>
        new("FJCH", 1, new Dictionary<string, string>
        {
            ["DEPTH"] = "STD",
            ["MECH"] = "NONE",
            ["HEAD"] = "HS1",
            ["STITCH"] = "PLAIN"
        }, fabricColorCode);

    private static ProductConfiguration FjchConfiguration(string fabricColorCode) =>
        new("FJORD", [FjchSelection(fabricColorCode)]);

    [Fact]
    public void Resolve_NoSuggestion_ReturnsComposedWithVariantAsEffectiveCodeAndNotExportable()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var configuration = FjchConfiguration("AQUA-BLUE");

        // Act
        var identities = ProductionIdentityResolver.Resolve(snapshot, configuration, new Dictionary<string, string>(), TradeItemState.Draft);

        // Assert
        var identity = Assert.Single(identities);
        Assert.Equal(ProductionCodeStatus.Composed, identity.Status);
        Assert.Null(identity.SuggestedCode);
        Assert.Equal(identity.VariantCode, identity.EffectiveCode);
        Assert.False(identity.IsExportable);
    }

    [Fact]
    public void Resolve_SuggestionUnderDraftModel_ReturnsProvisionalWithSuggestedCodeAsEffectiveAndNotExportable()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var configuration = FjchConfiguration("AQUA-BLUE");
        var variantCode = ProductionIdentityResolver.Resolve(snapshot, configuration, new Dictionary<string, string>(), TradeItemState.Draft)[0].VariantCode;
        var suggestions = new Dictionary<string, string> { [variantCode] = "18E" };

        // Act
        var identities = ProductionIdentityResolver.Resolve(snapshot, configuration, suggestions, TradeItemState.Draft);

        // Assert
        var identity = Assert.Single(identities);
        Assert.Equal(ProductionCodeStatus.Provisional, identity.Status);
        Assert.Equal("18E", identity.SuggestedCode);
        Assert.Equal("18E", identity.EffectiveCode);
        Assert.False(identity.IsExportable);
    }

    [Fact]
    public void Resolve_SuggestionUnderActiveModel_ReturnsReleasedAndExportable()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var configuration = FjchConfiguration("AQUA-BLUE");
        var variantCode = ProductionIdentityResolver.Resolve(snapshot, configuration, new Dictionary<string, string>(), TradeItemState.Draft)[0].VariantCode;
        var suggestions = new Dictionary<string, string> { [variantCode] = "18E" };

        // Act
        var identities = ProductionIdentityResolver.Resolve(snapshot, configuration, suggestions, TradeItemState.Active);

        // Assert
        var identity = Assert.Single(identities);
        Assert.Equal(ProductionCodeStatus.Released, identity.Status);
        Assert.Equal("18E", identity.EffectiveCode);
        Assert.True(identity.IsExportable);
    }

    [Fact]
    public void Resolve_SuggestionUnderDiscontinuedModel_ReturnsReleasedSemanticsButNotExportable()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var configuration = FjchConfiguration("AQUA-BLUE");
        var variantCode = ProductionIdentityResolver.Resolve(snapshot, configuration, new Dictionary<string, string>(), TradeItemState.Draft)[0].VariantCode;
        var suggestions = new Dictionary<string, string> { [variantCode] = "18E" };

        // Act
        var identities = ProductionIdentityResolver.Resolve(snapshot, configuration, suggestions, TradeItemState.Discontinued);

        // Assert
        var identity = Assert.Single(identities);
        Assert.Equal(ProductionCodeStatus.Released, identity.Status);
        Assert.Equal("18E", identity.EffectiveCode);
        Assert.False(identity.IsExportable);
    }

    [Fact]
    public void Resolve_SameFabricGroupDifferentColor_ProducesIdenticalVariantCodeButLeatherProducesDifferentCode()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var aquaBlue = FjchConfiguration("AQUA-BLUE");
        var aquaGreen = FjchConfiguration("AQUA-GREEN");
        var leatherThick = FjchConfiguration("HIDE-THICK-ESPRESSO");

        // Act
        var aquaBlueIdentity = ProductionIdentityResolver.Resolve(snapshot, aquaBlue, new Dictionary<string, string>(), TradeItemState.Draft)[0];
        var aquaGreenIdentity = ProductionIdentityResolver.Resolve(snapshot, aquaGreen, new Dictionary<string, string>(), TradeItemState.Draft)[0];
        var leatherThickIdentity = ProductionIdentityResolver.Resolve(snapshot, leatherThick, new Dictionary<string, string>(), TradeItemState.Draft)[0];

        // Assert
        Assert.Equal(aquaBlueIdentity.VariantCode, aquaGreenIdentity.VariantCode);
        Assert.NotEqual(aquaBlueIdentity.VariantCode, leatherThickIdentity.VariantCode);
        Assert.Equal("Fabric", aquaBlueIdentity.MaterialTypeCode);
        Assert.Equal("LEATHER-THICK", leatherThickIdentity.MaterialTypeCode);
    }

    [Fact]
    public void Resolve_BomSignificantSelections_ExcludesNonBomStitch_IncludesBomChoicesAndMaterial()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var configuration = FjchConfiguration("AQUA-BLUE");

        // Act
        var identity = Assert.Single(ProductionIdentityResolver.Resolve(snapshot, configuration, new Dictionary<string, string>(), TradeItemState.Draft));

        // Assert
        Assert.True(identity.BomSignificantSelections.ContainsKey("DEPTH"));
        Assert.True(identity.BomSignificantSelections.ContainsKey("MECH"));
        Assert.True(identity.BomSignificantSelections.ContainsKey(VariantCode.MaterialDefCode));
        Assert.False(identity.BomSignificantSelections.ContainsKey("STITCH"));
    }

    [Theory]
    [InlineData(false, TradeItemState.Draft, ProductionCodeStatus.Composed)]
    [InlineData(false, TradeItemState.Active, ProductionCodeStatus.Composed)]
    [InlineData(false, TradeItemState.Discontinued, ProductionCodeStatus.Composed)]
    [InlineData(true, TradeItemState.Draft, ProductionCodeStatus.Provisional)]
    [InlineData(true, TradeItemState.Active, ProductionCodeStatus.Released)]
    [InlineData(true, TradeItemState.Discontinued, ProductionCodeStatus.Released)]
    public void StatusFor_TruthTable(bool hasSuggestion, TradeItemState modelState, ProductionCodeStatus expected)
    {
        Assert.Equal(expected, ProductionIdentityResolver.StatusFor(hasSuggestion, modelState));
    }
}
