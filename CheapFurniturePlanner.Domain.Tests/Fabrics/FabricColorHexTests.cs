using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Fabrics;

// Hex is a display-only swatch colour for the configurator's colour picker. It must round-trip
// through serialization but must never influence pricing, since it plays no BOM/VariantCode role.
public class FabricColorHexTests
{
    [Fact]
    public void Serialize_FabricColorWithHex_RoundTripsHexValue()
    {
        // Arrange
        var color = new FabricColor { Code = "CF", Name = "Fabric Color", Hex = "#2E5E8C" };

        // Act
        var json = CanonicalJson.Serialize(color);
        var roundTripped = CanonicalJson.Deserialize<FabricColor>(json);

        // Assert
        Assert.Contains("\"Hex\":\"#2E5E8C\"", json);
        Assert.Equal("#2E5E8C", roundTripped!.Hex);
    }

    [Fact]
    public void Serialize_FabricColorWithoutHex_HexIsNull()
    {
        // Arrange
        var color = new FabricColor { Code = "CF", Name = "Fabric Color" };

        // Act
        var roundTripped = CanonicalJson.Deserialize<FabricColor>(CanonicalJson.Serialize(color));

        // Assert
        Assert.Null(roundTripped!.Hex);
    }

    private static MarketParameters CreateMarket(string code = "EU") =>
        new(code, TransportRatePerUnit: 1m, FixedCostPercent: 0.1m, MarkupSteps: [], Rounding: new RoundingPolicy(2, 2, MidpointRounding.AwayFromZero, RoundStage.Final));

    private static CatalogueSnapshot BuildSnapshot() => new()
    {
        Version = "1",
        Models =
        [
            new FurnitureModel
            {
                Code = "M",
                Name = "Model",
                Elements =
                [
                    new Element
                    {
                        Code = "E",
                        Name = "Element",
                        Options = [new FabricOption { OptionDefinitionCode = "FABRIC", FabricGroupCodes = ["GF"] }],
                        Bom = new BomDocument
                        {
                            Sections =
                            [
                                new BomSection
                                {
                                    Kind = BomSectionKind.CutSort,
                                    Lines = [new CutSortBomLine { LineKey = "FB", Metrage = 1m, CutUnits = 1m }]
                                }
                            ]
                        }
                    }
                ]
            }
        ],
        FabricGroups =
        [
            new FabricGroup
            {
                Code = "GF", PriceGroupCode = "PGF",
                Colors =
                [
                    new FabricColor { Code = "CF-HEX", Name = "Has Hex", Hex = "#2E5E8C" },
                    new FabricColor { Code = "CF-NOHEX", Name = "No Hex" }
                ]
            }
        ],
        PriceGroups = [new PriceGroup { Code = "PGF", Kind = MaterialKind.Fabric, RatePerMeter = 10m }],
        Markets = [CreateMarket()]
    };

    private static PricingRequest BuildRequest(CatalogueSnapshot snapshot, string fabricColorCode)
    {
        var selection = new ElementSelection("E", 1, new Dictionary<string, string>(), fabricColorCode);
        var configuration = new ProductConfiguration("M", [selection]);
        return new PricingRequest(snapshot, configuration, new PricingContext(CreateMarket()));
    }

    [Fact]
    public void Calculate_FabricColorsDifferingOnlyByHex_ProduceIdenticalVariantCodesAndTotals()
    {
        // Arrange
        var snapshot = BuildSnapshot();
        var requestWithHex = BuildRequest(snapshot, "CF-HEX");
        var requestWithoutHex = BuildRequest(snapshot, "CF-NOHEX");

        // Act
        var resultWithHex = PricingEngine.Calculate(requestWithHex);
        var resultWithoutHex = PricingEngine.Calculate(requestWithoutHex);

        // Assert
        Assert.True(resultWithHex.IsSuccess, string.Join(", ", resultWithHex.Errors.Select(e => $"{e.Kind}:{e.Subject}")));
        Assert.True(resultWithoutHex.IsSuccess, string.Join(", ", resultWithoutHex.Errors.Select(e => $"{e.Kind}:{e.Subject}")));
        var elementWithHex = Assert.Single(resultWithHex.Breakdown!.Elements);
        var elementWithoutHex = Assert.Single(resultWithoutHex.Breakdown!.Elements);
        Assert.Equal(elementWithoutHex.VariantCode, elementWithHex.VariantCode);
        Assert.Equal(elementWithoutHex.ElementTotal, elementWithHex.ElementTotal);
    }
}
