using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Pricing.Engine;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Pricing;

public class ResolveStageTests
{
    private static MarketParameters CreateMarket(string code = "EU") =>
        new(code, TransportRatePerUnit: 1m, FixedCostPercent: 0.1m, MarkupSteps: [], Rounding: new RoundingPolicy(2, 2, MidpointRounding.AwayFromZero, RoundStage.Final));

    [Fact]
    public void Run_HappyPath_ResolvesElementWithFilteredLinesSubstitutionAndVariantCode()
    {
        // Arrange
        var element = new Element
        {
            Code = "SEAT",
            Name = "Seat",
            Options =
            [
                new ChoiceOption
                {
                    OptionDefinitionCode = "COLOR",
                    Required = true,
                    AffectsBom = true,
                    Values = [new ProductOptionValue { OptionChoiceCode = "RED" }, new ProductOptionValue { OptionChoiceCode = "BLUE" }]
                },
                new FabricOption
                {
                    OptionDefinitionCode = "FABRIC",
                    FabricGroupCodes = ["GRP1"]
                }
            ],
            Bom = new BomDocument
            {
                Sections =
                [
                    new BomSection
                    {
                        Kind = BomSectionKind.Foam,
                        Lines =
                        [
                            new FoamBomLine { LineKey = "F1", FoamCode = "FOAM-STD", Quantity = 1m },
                            new FoamBomLine { LineKey = "F2", FoamCode = "FOAM-PREMIUM", Condition = new ApplicabilityCondition([new SelectionKey("COLOR", "BLUE")]) }
                        ]
                    },
                    new BomSection
                    {
                        Kind = BomSectionKind.Misc,
                        Lines = [new MiscBomLine { LineKey = "M1", MaterialCode = "GLUE" }]
                    }
                ]
            },
            Substitutions =
            [
                new SubstitutionRule(new ApplicabilityCondition([new SelectionKey("COLOR", "RED")]), ReplaceMaterialCode: "FOAM-STD", WithMaterialCode: "FOAM-RED-SPECIAL", QuantityOverride: 2m)
            ]
        };

        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] };

        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            Models = [model],
            FabricGroups = [new FabricGroup { Code = "GRP1", PriceGroupCode = "PG1", Colors = [new FabricColor { Code = "CRIMSON", Name = "Crimson" }] }],
            PriceGroups = [new PriceGroup { Code = "PG1", Kind = MaterialKind.Fabric, RatePerMeter = 10m }],
            Markets = [CreateMarket()]
        };

        var selection = new ElementSelection("SEAT", 1, new Dictionary<string, string> { { "COLOR", "RED" } }, "CRIMSON");
        var configuration = new ProductConfiguration("SOFA", [selection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(errors);
        var resolvedElement = Assert.Single(resolved);
        Assert.Equal("SEAT-COLOR:RED", resolvedElement.VariantCodeValue);
        Assert.Equal("PG1", resolvedElement.ResolvedPriceGroup.Code);
        Assert.Equal(2, resolvedElement.EffectiveLines.Count);
        var replacedFoam = Assert.IsType<FoamBomLine>(resolvedElement.EffectiveLines.Single(l => l.LineKey == "F1"));
        Assert.Equal("FOAM-RED-SPECIAL", replacedFoam.FoamCode);
        Assert.Equal(2m, replacedFoam.Quantity);
        Assert.Contains(resolvedElement.EffectiveLines, l => l.LineKey == "M1");
        Assert.DoesNotContain(resolvedElement.EffectiveLines, l => l.LineKey == "F2");
    }

    [Fact]
    public void Run_ElementWithoutFabricOption_ResolvesWithSentinelPriceGroup()
    {
        // Arrange
        var element = new Element
        {
            Code = "TABLE",
            Name = "Table",
            Options = []
        };
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] };
        var snapshot = new CatalogueSnapshot { Version = "1", Models = [model], Markets = [CreateMarket()] };
        var selection = new ElementSelection("TABLE", 1, new Dictionary<string, string>(), null);
        var configuration = new ProductConfiguration("SOFA", [selection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(errors);
        var resolvedElement = Assert.Single(resolved);
        Assert.Equal("", resolvedElement.ResolvedPriceGroup.Code);
        Assert.Equal(0m, resolvedElement.ResolvedPriceGroup.RatePerMeter);
    }

    [Fact]
    public void Run_UnknownModelCode_ReturnsUnknownModelError()
    {
        // Arrange
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [] };
        var snapshot = new CatalogueSnapshot { Version = "1", Models = [model], Markets = [CreateMarket()] };
        var configuration = new ProductConfiguration("DOES-NOT-EXIST", []);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(resolved);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.UnknownModel, error.Kind);
        Assert.Equal("DOES-NOT-EXIST", error.Subject);
    }

    [Fact]
    public void Run_UnknownElementCode_ReturnsUnknownElementError()
    {
        // Arrange
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [] };
        var snapshot = new CatalogueSnapshot { Version = "1", Models = [model], Markets = [CreateMarket()] };
        var selection = new ElementSelection("SEAT", 1, new Dictionary<string, string>(), null);
        var configuration = new ProductConfiguration("SOFA", [selection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(resolved);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.UnknownElement, error.Kind);
        Assert.Equal("SEAT", error.Subject);
    }

    [Fact]
    public void Run_MissingRequiredVisibleChoice_ReturnsIncompleteConfigurationError()
    {
        // Arrange
        var element = new Element
        {
            Code = "SEAT",
            Name = "Seat",
            Options = [new ChoiceOption { OptionDefinitionCode = "COLOR", Required = true, Values = [new ProductOptionValue { OptionChoiceCode = "RED" }] }]
        };
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] };
        var snapshot = new CatalogueSnapshot { Version = "1", Models = [model], Markets = [CreateMarket()] };
        var selection = new ElementSelection("SEAT", 1, new Dictionary<string, string>(), null);
        var configuration = new ProductConfiguration("SOFA", [selection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(resolved);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.IncompleteConfiguration, error.Kind);
        Assert.Equal("SEAT:COLOR", error.Subject);
    }

    [Fact]
    public void Run_SelectionForUnknownOptionDefinition_ReturnsUnknownOptionSelectionError()
    {
        // Arrange
        var element = new Element
        {
            Code = "SEAT",
            Name = "Seat",
            Options = [new ChoiceOption { OptionDefinitionCode = "COLOR", Required = false, Values = [new ProductOptionValue { OptionChoiceCode = "RED" }] }]
        };
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] };
        var snapshot = new CatalogueSnapshot { Version = "1", Models = [model], Markets = [CreateMarket()] };
        var selection = new ElementSelection("SEAT", 1, new Dictionary<string, string> { { "SIZE", "LARGE" } }, null);
        var configuration = new ProductConfiguration("SOFA", [selection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(resolved);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.UnknownOptionSelection, error.Kind);
        Assert.Equal("SEAT:SIZE=LARGE", error.Subject);
    }

    [Fact]
    public void Run_SelectionForNonVisibleOption_ReturnsSelectionViolatesVisibilityError()
    {
        // Arrange
        var element = new Element
        {
            Code = "SEAT",
            Name = "Seat",
            Options =
            [
                new ChoiceOption
                {
                    OptionDefinitionCode = "COLOR",
                    Required = false,
                    Values = [new ProductOptionValue { OptionChoiceCode = "RED" }],
                    VisibilityRules = [new VisibilityRule("SIZE", "LARGE", "COLOR")]
                }
            ]
        };
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] };
        var snapshot = new CatalogueSnapshot { Version = "1", Models = [model], Markets = [CreateMarket()] };
        var selection = new ElementSelection("SEAT", 1, new Dictionary<string, string> { { "COLOR", "RED" } }, null);
        var configuration = new ProductConfiguration("SOFA", [selection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(resolved);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.SelectionViolatesVisibility, error.Kind);
        Assert.Equal("SEAT:COLOR", error.Subject);
    }

    [Fact]
    public void Run_MissingFabricColorForFabricOption_ReturnsIncompleteConfigurationError()
    {
        // Arrange
        var element = new Element
        {
            Code = "SEAT",
            Name = "Seat",
            Options = [new FabricOption { OptionDefinitionCode = "FABRIC", FabricGroupCodes = ["GRP1"] }]
        };
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] };
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            Models = [model],
            FabricGroups = [new FabricGroup { Code = "GRP1", PriceGroupCode = "PG1", Colors = [new FabricColor { Code = "CRIMSON", Name = "Crimson" }] }],
            PriceGroups = [new PriceGroup { Code = "PG1", Kind = MaterialKind.Fabric, RatePerMeter = 10m }],
            Markets = [CreateMarket()]
        };
        var selection = new ElementSelection("SEAT", 1, new Dictionary<string, string>(), null);
        var configuration = new ProductConfiguration("SOFA", [selection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(resolved);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.IncompleteConfiguration, error.Kind);
        Assert.Equal("SEAT:FABRIC", error.Subject);
    }

    [Fact]
    public void Run_FabricColorNotInAnyGroup_ReturnsUnknownFabricColorError()
    {
        // Arrange
        var element = new Element
        {
            Code = "SEAT",
            Name = "Seat",
            Options = [new FabricOption { OptionDefinitionCode = "FABRIC", FabricGroupCodes = ["GRP1"] }]
        };
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] };
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            Models = [model],
            FabricGroups = [new FabricGroup { Code = "GRP1", PriceGroupCode = "PG1", Colors = [new FabricColor { Code = "CRIMSON", Name = "Crimson" }] }],
            PriceGroups = [new PriceGroup { Code = "PG1", Kind = MaterialKind.Fabric, RatePerMeter = 10m }],
            Markets = [CreateMarket()]
        };
        var selection = new ElementSelection("SEAT", 1, new Dictionary<string, string>(), "TEAL");
        var configuration = new ProductConfiguration("SOFA", [selection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(resolved);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.UnknownFabricColor, error.Kind);
        Assert.Equal("SEAT:TEAL", error.Subject);
    }

    [Fact]
    public void Run_FabricGroupMissingPriceGroup_ReturnsNoPriceGroupForMaterialKindError()
    {
        // Arrange
        var element = new Element
        {
            Code = "SEAT",
            Name = "Seat",
            Options = [new FabricOption { OptionDefinitionCode = "FABRIC", FabricGroupCodes = ["GRP1"] }]
        };
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] };
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            Models = [model],
            FabricGroups = [new FabricGroup { Code = "GRP1", PriceGroupCode = "PG-MISSING", Colors = [new FabricColor { Code = "CRIMSON", Name = "Crimson" }] }],
            PriceGroups = [],
            Markets = [CreateMarket()]
        };
        var selection = new ElementSelection("SEAT", 1, new Dictionary<string, string>(), "CRIMSON");
        var configuration = new ProductConfiguration("SOFA", [selection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        Assert.Empty(resolved);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.NoPriceGroupForMaterialKind, error.Kind);
        Assert.Equal("SEAT:PG-MISSING", error.Subject);
    }

    [Fact]
    public void Run_MarketNotInSnapshot_ReturnsUnknownMarketError()
    {
        // Arrange
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [] };
        var snapshot = new CatalogueSnapshot { Version = "1", Models = [model], Markets = [CreateMarket("EU")] };
        var configuration = new ProductConfiguration("SOFA", []);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket("US")));

        // Act
        var (resolved, errors) = ResolveStage.Run(request);

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.UnknownMarket, error.Kind);
        Assert.Equal("US", error.Subject);
    }
}
