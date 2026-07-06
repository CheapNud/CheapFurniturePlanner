using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Pricing;

// Material type (fabric vs leather vs thick-leather) is BOM-significant: a BOM line can condition on
// it via the synthetic __MATERIAL__ selection, and it is baked into the VariantCode. Color is not.
public class MaterialTypeTests
{
    private static MarketParameters CreateMarket(string code = "EU") =>
        new(code, TransportRatePerUnit: 1m, FixedCostPercent: 0.1m, MarkupSteps: [], Rounding: new RoundingPolicy(2, 2, MidpointRounding.AwayFromZero, RoundStage.Final));

    private static CatalogueSnapshot BuildSnapshot()
    {
        var element = new Element
        {
            Code = "E",
            Name = "Element",
            Options =
            [
                new FabricOption
                {
                    OptionDefinitionCode = "FABRIC",
                    FabricGroupCodes = ["GF", "GL"]
                }
            ],
            Bom = new BomDocument
            {
                Sections =
                [
                    new BomSection
                    {
                        Kind = BomSectionKind.Frame,
                        Lines = [new FrameBomLine { LineKey = "FR", FrameBodyCode = "FBX" }]
                    },
                    new BomSection
                    {
                        Kind = BomSectionKind.Labor,
                        Lines =
                        [
                            new LaborBomLine
                            {
                                LineKey = "LB-LEATHER",
                                OperationCode = "OP-LEATHER",
                                Units = 1m,
                                Condition = new ApplicabilityCondition([new SelectionKey("__MATERIAL__", "LEATHER-THICK")])
                            }
                        ]
                    },
                    new BomSection
                    {
                        Kind = BomSectionKind.CutSort,
                        Lines = [new CutSortBomLine { LineKey = "FB", Metrage = 1m, CutUnits = 1m }]
                    }
                ]
            }
        };

        var model = new FurnitureModel { Code = "M", Name = "Model", Elements = [element] };

        return new CatalogueSnapshot
        {
            Version = "1",
            Models = [model],
            FabricGroups =
            [
                new FabricGroup
                {
                    Code = "GF", PriceGroupCode = "PGF",
                    Colors = [new FabricColor { Code = "CF", Name = "Fabric Color" }, new FabricColor { Code = "CF2", Name = "Fabric Color 2" }]
                },
                new FabricGroup
                {
                    Code = "GL", PriceGroupCode = "PGL",
                    Colors = [new FabricColor { Code = "CL", Name = "Leather Color" }]
                }
            ],
            PriceGroups =
            [
                new PriceGroup { Code = "PGF", Kind = MaterialKind.Fabric, RatePerMeter = 10m, MaterialTypeCode = null },
                new PriceGroup { Code = "PGL", Kind = MaterialKind.Leather, RatePerMeter = 40m, MaterialTypeCode = "LEATHER-THICK" }
            ],
            Operations = [new Operation("OP-LEATHER", "Leather operation", 15m)],
            FrameBodies = [new FrameBody("FBX", 50m, 0m, 0m, 0m)],
            Markets = [CreateMarket()]
        };
    }

    private static PricingRequest BuildRequest(CatalogueSnapshot snapshot, string fabricColorCode)
    {
        var selection = new ElementSelection("E", 1, new Dictionary<string, string>(), fabricColorCode);
        var configuration = new ProductConfiguration("M", [selection]);
        return new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));
    }

    [Fact]
    public void Calculate_FabricColor_HasNoLeatherLaborLineAndMaterialVariantSegmentIsFabricKind()
    {
        // Arrange
        var snapshot = BuildSnapshot();
        var request = BuildRequest(snapshot, "CF");

        // Act
        var result = PricingEngine.Calculate(request);

        // Assert
        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => $"{e.Kind}:{e.Subject}")));
        var element = Assert.Single(result.Breakdown!.Elements);
        Assert.Equal("E-__MATERIAL__:Fabric", element.VariantCode);
        Assert.DoesNotContain(element.Lines, l => l.Description.Contains("OP-LEATHER"));
    }

    [Fact]
    public void Calculate_LeatherColor_HasLeatherLaborLineSurvivingBomFilterAndMaterialVariantSegmentIsMaterialTypeCode()
    {
        // Arrange
        var snapshot = BuildSnapshot();
        var request = BuildRequest(snapshot, "CL");

        // Act
        var result = PricingEngine.Calculate(request);

        // Assert
        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => $"{e.Kind}:{e.Subject}")));
        var element = Assert.Single(result.Breakdown!.Elements);
        Assert.Equal("E-__MATERIAL__:LEATHER-THICK", element.VariantCode);
        Assert.Contains(element.Lines, l => l.Description.Contains("OP-LEATHER"));
    }

    [Fact]
    public void Calculate_TwoFabricColorsInSameGroup_ProduceIdenticalVariantCodesAndUnitPrices()
    {
        // Arrange
        var snapshot = BuildSnapshot();
        var requestCf = BuildRequest(snapshot, "CF");
        var requestCf2 = BuildRequest(snapshot, "CF2");

        // Act
        var resultCf = PricingEngine.Calculate(requestCf);
        var resultCf2 = PricingEngine.Calculate(requestCf2);

        // Assert
        Assert.True(resultCf.IsSuccess);
        Assert.True(resultCf2.IsSuccess);
        var elementCf = Assert.Single(resultCf.Breakdown!.Elements);
        var elementCf2 = Assert.Single(resultCf2.Breakdown!.Elements);
        Assert.Equal(elementCf.VariantCode, elementCf2.VariantCode);
        Assert.Equal(elementCf.ElementTotal, elementCf2.ElementTotal);
    }
}
