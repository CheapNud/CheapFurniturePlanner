using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Pricing;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Pricing;

public class PricingEngineTests
{
    private static readonly RoundingPolicy Rounding = new(2, 2, MidpointRounding.AwayFromZero, RoundStage.Line | RoundStage.Subtotal | RoundStage.Final);

    // Single Labor BOM line (10 units * 5.00 = 50.00) is the only cost-stage line, so costBase is
    // trivially hand-computable: 50.00 -> overhead 5.00 (10%) -> transport 6.00 (2 units * 3.00) -> chainBase 61.00.
    private static Element CreateElement(string code = "SEAT", int transportUnits = 2, string operationCode = "OP1") => new()
    {
        Code = code,
        Name = code,
        TransportUnits = transportUnits,
        Bom = new BomDocument
        {
            Sections =
            [
                new BomSection
                {
                    Kind = BomSectionKind.Labor,
                    Lines = [new LaborBomLine { LineKey = "LB1", OperationCode = operationCode, Units = 10m }]
                }
            ]
        }
    };

    private static PricingRequest CreateRequest(Element element, MarketParameters market, decimal sellerMultiplier = 1m)
    {
        var model = new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] };
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            ContentHash = "HASH1",
            Models = [model],
            Operations = [new Operation("OP1", "Sew", 5.00m)],
            Markets = [market]
        };
        var selection = new ElementSelection(element.Code, 1, new Dictionary<string, string>(), null);
        var configuration = new ProductConfiguration("SOFA", [selection]);
        return new PricingRequest(snapshot, configuration, new PricingContext(market, sellerMultiplier));
    }

    [Fact]
    public void Calculate_CompoundOnlyMarkupChain_ComputesEveryStageSubtotalTraceAndFinal()
    {
        // Arrange
        var market = new MarketParameters(
            "EU", TransportRatePerUnit: 3.00m, FixedCostPercent: 10m,
            MarkupSteps: [new MarkupStep("Retail", 20m, MarkupMode.Compound), new MarkupStep("Extra", 10m, MarkupMode.Compound)],
            Rounding: Rounding);
        var request = CreateRequest(CreateElement(), market);

        // Act
        var result = PricingEngine.Calculate(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Breakdown);
        Assert.Equal("1", result.Breakdown!.CatalogueVersion);
        Assert.Equal("HASH1", result.Breakdown.ContentHash);
        Assert.Equal("EU", result.Breakdown.MarketCode);

        var element = Assert.Single(result.Breakdown.Elements);
        Assert.Equal("SEAT", element.ElementCode);
        Assert.Equal(1, element.Quantity);

        Assert.Equal(50.00m, element.StageSubtotals["Labor"]);
        Assert.Equal(5.00m, element.StageSubtotals["Overhead"]);
        Assert.Equal(6.00m, element.StageSubtotals["Transport"]);
        Assert.Equal(61.00m, element.StageSubtotals["ChainBase"]);
        Assert.Equal(80.52m, element.StageSubtotals["UnitPrice"]);
        Assert.DoesNotContain("Materials", element.StageSubtotals.Keys);
        Assert.DoesNotContain("Fabric", element.StageSubtotals.Keys);
        Assert.DoesNotContain("Surcharges", element.StageSubtotals.Keys);

        Assert.Equal(2, element.MarkupTrace.Count);
        Assert.Equal(new MarkupTraceEntry("Retail", 20m, "Compound", 73.20m), element.MarkupTrace[0]);
        Assert.Equal(new MarkupTraceEntry("Extra", 10m, "Compound", 80.52m), element.MarkupTrace[1]);

        Assert.Equal(80.52m, element.ElementTotal);
        Assert.Equal(80.52m, result.Breakdown.DocumentTotal);

        // Lines = cost lines (Labor) + overhead + transport, in that order.
        Assert.Equal(3, element.Lines.Count);
        Assert.Equal(BreakdownStage.Labor, element.Lines[0].Stage);
        Assert.Equal(BreakdownStage.Overhead, element.Lines[1].Stage);
        Assert.Equal("overhead", element.Lines[1].Category);
        Assert.Equal(5.00m, element.Lines[1].LineTotal);
        Assert.Equal(BreakdownStage.Transport, element.Lines[2].Stage);
        Assert.Equal("transport", element.Lines[2].Category);
        Assert.Equal(2m, element.Lines[2].Quantity);
        Assert.Equal(3.00m, element.Lines[2].UnitCost);
        Assert.Equal(6.00m, element.Lines[2].LineTotal);
    }

    [Fact]
    public void Calculate_AdditiveMarkupChain_BothStepsComputeOffChainBase()
    {
        // Arrange: same costBase/overhead/transport as the compound test (chainBase 61.00), but two
        // additive steps that must both be computed against chainBase, never against the running total.
        var market = new MarketParameters(
            "EU", TransportRatePerUnit: 3.00m, FixedCostPercent: 10m,
            MarkupSteps: [new MarkupStep("Fee1", 5m, MarkupMode.Additive), new MarkupStep("Fee2", 3m, MarkupMode.Additive)],
            Rounding: Rounding);
        var request = CreateRequest(CreateElement(), market);

        // Act
        var result = PricingEngine.Calculate(request);

        // Assert
        Assert.NotNull(result.Breakdown);
        var element = Assert.Single(result.Breakdown!.Elements);

        Assert.Equal(61.00m, element.StageSubtotals["ChainBase"]);
        Assert.Equal(2, element.MarkupTrace.Count);
        Assert.Equal(new MarkupTraceEntry("Fee1", 5m, "Additive", 64.05m), element.MarkupTrace[0]);
        Assert.Equal(new MarkupTraceEntry("Fee2", 3m, "Additive", 65.88m), element.MarkupTrace[1]);
        Assert.Equal(65.88m, element.StageSubtotals["UnitPrice"]);
        Assert.Equal(65.88m, element.ElementTotal);
        Assert.Equal(65.88m, result.Breakdown.DocumentTotal);
    }

    [Fact]
    public void Calculate_SellerMultiplierNotOne_AppliesAfterMarkupChainWithoutTraceEntry()
    {
        // Arrange: same compound chain as the first test (running reaches 80.52 before the multiplier),
        // then a 1.1 seller multiplier is applied - it must not appear as a MarkupTrace entry.
        var market = new MarketParameters(
            "EU", TransportRatePerUnit: 3.00m, FixedCostPercent: 10m,
            MarkupSteps: [new MarkupStep("Retail", 20m, MarkupMode.Compound), new MarkupStep("Extra", 10m, MarkupMode.Compound)],
            Rounding: Rounding);
        var request = CreateRequest(CreateElement(), market, sellerMultiplier: 1.1m);

        // Act
        var result = PricingEngine.Calculate(request);

        // Assert
        Assert.NotNull(result.Breakdown);
        var element = Assert.Single(result.Breakdown!.Elements);

        Assert.Equal(2, element.MarkupTrace.Count);
        Assert.Equal(new MarkupTraceEntry("Retail", 20m, "Compound", 73.20m), element.MarkupTrace[0]);
        Assert.Equal(new MarkupTraceEntry("Extra", 10m, "Compound", 80.52m), element.MarkupTrace[1]);

        // 80.52 * 1.1 = 88.572 -> RoundFinal(2 decimals, AwayFromZero) = 88.57.
        Assert.Equal(88.57m, element.StageSubtotals["UnitPrice"]);
        Assert.Equal(88.57m, element.ElementTotal);
        Assert.Equal(88.57m, result.Breakdown.DocumentTotal);
    }

    [Fact]
    public void Calculate_CostStageError_ReturnsErrorsOnlyResultWithNullBreakdown()
    {
        // Arrange: unknown operation code triggers a CostStages error.
        var market = new MarketParameters("EU", TransportRatePerUnit: 3.00m, FixedCostPercent: 10m, MarkupSteps: [], Rounding: Rounding);
        var request = CreateRequest(CreateElement(operationCode: "MISSING"), market);

        // Act
        var result = PricingEngine.Calculate(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Breakdown);
        var error = Assert.Single(result.Errors);
        Assert.Equal(PricingErrorKind.UnknownOperation, error.Kind);
        Assert.Equal("MISSING", error.Subject);
    }
}
