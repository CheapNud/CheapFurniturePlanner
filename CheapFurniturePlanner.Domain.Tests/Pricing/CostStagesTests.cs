using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Pricing.Engine;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Pricing;

public class CostStagesTests
{
    private static readonly RoundingPolicy Rounding = new(2, 2, MidpointRounding.AwayFromZero, RoundStage.Line);

    private static Element CreateElement(string code = "SEAT") => new() { Code = code, Name = code };

    private static ElementSelection CreateSelection(string elementCode = "SEAT", IReadOnlyDictionary<string, string>? choices = null) =>
        new(elementCode, 1, choices ?? new Dictionary<string, string>(), null);

    private static ResolvedElement CreateResolved(Element element, ElementSelection selection, IReadOnlyList<BomLine> lines, PriceGroup? priceGroup = null) =>
        new(element, selection, "VARIANT", priceGroup ?? new PriceGroup { Code = "", Kind = MaterialKind.Fabric, RatePerMeter = 0m },
            lines.Select(line => new EffectiveLine(SectionKindFor(line), line)).ToList());

    private static BomSectionKind SectionKindFor(BomLine line) => line switch
    {
        FrameBomLine => BomSectionKind.Frame,
        FoamBomLine => BomSectionKind.Foam,
        CottonBomLine => BomSectionKind.Cotton,
        CutSortBomLine => BomSectionKind.CutSort,
        MiscBomLine => BomSectionKind.Misc,
        LaborBomLine => BomSectionKind.Labor,
        _ => throw new ArgumentOutOfRangeException(nameof(line))
    };

    [Fact]
    public void Run_FrameLine_ComputesPriceFixedSurchargeAndSprayLines()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine> { new FrameBomLine { LineKey = "FR1", FrameBodyCode = "BODY1", Colored = true } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            FrameBodies = [new FrameBody("BODY1", 120.00m, 0m, 0m, 0m)],
            FixedSurcharges = [new FixedSurcharge("Handling", BomSectionKind.Frame, 5.00m)],
            SprayPrices = [new SprayPrice("BODY1", 12.50m)]
        };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        Assert.Equal(3, result.Count);

        var frame = Assert.Single(result, l => l.Category == "frame");
        Assert.Equal(BreakdownStage.Materials, frame.Stage);
        Assert.Equal("FR1", frame.SourceLineKey);
        Assert.Equal(1m, frame.Quantity);
        Assert.Equal("pc", frame.Unit);
        Assert.Equal(120.00m, frame.UnitCost);
        Assert.Equal(120.00m, frame.LineTotal);

        var surcharge = Assert.Single(result, l => l.Category == "surcharge");
        Assert.Equal(BreakdownStage.Materials, surcharge.Stage);
        Assert.Null(surcharge.SourceLineKey);
        Assert.Equal("pc", surcharge.Unit);
        Assert.Equal(5.00m, surcharge.LineTotal);

        var spray = Assert.Single(result, l => l.Category == "spray");
        Assert.Equal(BreakdownStage.Materials, spray.Stage);
        Assert.Null(spray.SourceLineKey);
        Assert.Equal("pc", spray.Unit);
        Assert.Equal(12.50m, spray.LineTotal);
    }

    [Fact]
    public void Run_FixedSurchargeAppliesToSectionMatchingFoamLine_EmitsSurchargeForFoamLine()
    {
        // Arrange: a FixedSurcharge scoped to Foam must attach to a Foam BOM line, not just Frame.
        var element = CreateElement();
        var lines = new List<BomLine> { new FoamBomLine { LineKey = "FM1", FoamCode = "FOAM1", Quantity = 2m } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            Materials = [new Material("FOAM1", "Foam Std", 8.00m, "pc")],
            FixedSurcharges = [new FixedSurcharge("Foam handling", BomSectionKind.Foam, 3.00m)]
        };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        Assert.Equal(2, result.Count);
        var surcharge = Assert.Single(result, l => l.Category == "surcharge");
        Assert.Equal(BreakdownStage.Materials, surcharge.Stage);
        Assert.Equal(3.00m, surcharge.LineTotal);
    }

    [Fact]
    public void Run_FoamLineErrors_DoesNotEmitFixedSurchargeForThatLine()
    {
        // Arrange: a surcharge scoped to Foam must not be emitted when the Foam line itself failed to resolve.
        var element = CreateElement();
        var lines = new List<BomLine> { new FoamBomLine { LineKey = "FM1", FoamCode = "MISSING" } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            FixedSurcharges = [new FixedSurcharge("Foam handling", BomSectionKind.Foam, 3.00m)]
        };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Single(errors);
        Assert.Empty(result);
    }

    [Fact]
    public void Run_FrameLineNotColored_DoesNotEmitSprayLine()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine> { new FrameBomLine { LineKey = "FR1", FrameBodyCode = "BODY1", Colored = false } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            FrameBodies = [new FrameBody("BODY1", 120.00m, 0m, 0m, 0m)],
            SprayPrices = [new SprayPrice("BODY1", 12.50m)]
        };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        Assert.DoesNotContain(result, l => l.Category == "spray");
    }

    [Fact]
    public void Run_FrameLineUnknownFrameBody_ReturnsUnknownFrameBodyErrorAndNoLines()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine> { new FrameBomLine { LineKey = "FR1", FrameBodyCode = "MISSING" } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot { Version = "1" };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(result);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.UnknownFrameBody, error.Kind);
        Assert.Equal("MISSING", error.Subject);
    }

    [Fact]
    public void Run_FoamLine_ComputesUnitCostTimesQuantity()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine> { new FoamBomLine { LineKey = "FM1", FoamCode = "FOAM1", Quantity = 3m } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot { Version = "1", Materials = [new Material("FOAM1", "Foam Std", 8.00m, "pc")] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        var foam = Assert.Single(result);
        Assert.Equal(BreakdownStage.Materials, foam.Stage);
        Assert.Equal("foam", foam.Category);
        Assert.Equal("FM1", foam.SourceLineKey);
        Assert.Equal(3m, foam.Quantity);
        Assert.Equal("pc", foam.Unit);
        Assert.Equal(8.00m, foam.UnitCost);
        Assert.Equal(24.00m, foam.LineTotal);
    }

    [Fact]
    public void Run_FoamLineUnknownMaterial_ReturnsUnknownMaterialError()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine> { new FoamBomLine { LineKey = "FM1", FoamCode = "MISSING" } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot { Version = "1" };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(result);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.UnknownMaterial, error.Kind);
        Assert.Equal("MISSING", error.Subject);
    }

    [Fact]
    public void Run_CottonLine_ComputesUnitCostTimesMeasurementDividedByConversionFactor_IgnoringQuantity()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine>
        {
            new CottonBomLine { LineKey = "CT1", CottonQualityCode = "COT1", Measurement = 2.5m, CutUnits = 1m, UnitConversionFactor = 2m, Quantity = 999m }
        };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot { Version = "1", Materials = [new Material("COT1", "Cotton A", 5.00m, "m")] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        var cotton = Assert.Single(result);
        Assert.Equal(BreakdownStage.Materials, cotton.Stage);
        Assert.Equal("cotton", cotton.Category);
        Assert.Equal("CT1", cotton.SourceLineKey);
        Assert.Equal(2.5m, cotton.Quantity); // Measurement is the driver, not BomLine.Quantity
        Assert.Equal("m", cotton.Unit);
        Assert.Equal(6.25m, cotton.LineTotal);
    }

    [Fact]
    public void Run_MiscLine_ComputesUnitCostTimesQuantityDividedByConversionFactor()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine> { new MiscBomLine { LineKey = "MI1", MaterialCode = "GLUE", Quantity = 10m, UnitConversionFactor = 4m } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot { Version = "1", Materials = [new Material("GLUE", "Glue", 1.20m, "pc")] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        var misc = Assert.Single(result);
        Assert.Equal(BreakdownStage.Materials, misc.Stage);
        Assert.Equal("misc", misc.Category);
        Assert.Equal("MI1", misc.SourceLineKey);
        Assert.Equal(10m, misc.Quantity);
        Assert.Equal("pc", misc.Unit);
        Assert.Equal(3.00m, misc.LineTotal);
    }

    [Fact]
    public void Run_MiscLine_RoundsLineTotalThroughRoundingPolicy()
    {
        // Arrange: 1.00 / 3 = 0.3333...(repeating) - only comes out to 0.33 if RoundLine is actually applied.
        var element = CreateElement();
        var lines = new List<BomLine> { new MiscBomLine { LineKey = "MI1", MaterialCode = "GLUE", Quantity = 1m, UnitConversionFactor = 3m } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot { Version = "1", Materials = [new Material("GLUE", "Glue", 1.00m, "pc")] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        var misc = Assert.Single(result);
        Assert.Equal(0.33m, misc.LineTotal);
    }

    [Fact]
    public void Run_FabricLine_ComputesPrimaryAndSecondaryMetrages()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine>
        {
            new CutSortBomLine { LineKey = "FB1", Metrage = 4.2m, CutUnits = 1m, SecondaryGroupMetrages = new Dictionary<string, decimal> { ["PG2"] = 0.8m } }
        };
        var priceGroup = new PriceGroup { Code = "PG1", Kind = MaterialKind.Fabric, RatePerMeter = 21.50m };
        var resolved = CreateResolved(element, CreateSelection(), lines, priceGroup);
        var snapshot = new CatalogueSnapshot { Version = "1", PriceGroups = [priceGroup, new PriceGroup { Code = "PG2", Kind = MaterialKind.Fabric, RatePerMeter = 34.00m }] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        Assert.Equal(2, result.Count);

        var primary = Assert.Single(result, l => l.Category == "fabric");
        Assert.Equal(BreakdownStage.Fabric, primary.Stage);
        Assert.Equal("FB1", primary.SourceLineKey);
        Assert.Equal(4.2m, primary.Quantity);
        Assert.Equal("m", primary.Unit);
        Assert.Equal(21.50m, primary.UnitCost);
        Assert.Equal(90.30m, primary.LineTotal);

        var secondary = Assert.Single(result, l => l.Category == "fabric-secondary");
        Assert.Equal(BreakdownStage.Fabric, secondary.Stage);
        Assert.Equal("FB1", secondary.SourceLineKey);
        Assert.Equal(0.8m, secondary.Quantity);
        Assert.Equal("m", secondary.Unit);
        Assert.Equal(34.00m, secondary.UnitCost);
        Assert.Equal(27.20m, secondary.LineTotal);
    }

    [Fact]
    public void Run_FabricLineSecondaryGroupMissing_ReturnsNoPriceGroupForMaterialKindErrorAndStillEmitsPrimary()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine>
        {
            new CutSortBomLine { LineKey = "FB1", Metrage = 4.2m, SecondaryGroupMetrages = new Dictionary<string, decimal> { ["MISSING"] = 1.0m } }
        };
        var priceGroup = new PriceGroup { Code = "PG1", Kind = MaterialKind.Fabric, RatePerMeter = 21.50m };
        var resolved = CreateResolved(element, CreateSelection(), lines, priceGroup);
        var snapshot = new CatalogueSnapshot { Version = "1", PriceGroups = [priceGroup] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.NoPriceGroupForMaterialKind, error.Kind);
        Assert.Equal("MISSING", error.Subject);
        Assert.Single(result, l => l.Category == "fabric");
        Assert.DoesNotContain(result, l => l.Category == "fabric-secondary");
    }

    [Fact]
    public void Run_LaborLine_ComputesUnitsTimesOperationUnitCost()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine> { new LaborBomLine { LineKey = "LB1", OperationCode = "OP1", Units = 6m } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot { Version = "1", Operations = [new Operation("OP1", "Cut", 1.75m)] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        var labor = Assert.Single(result);
        Assert.Equal(BreakdownStage.Labor, labor.Stage);
        Assert.Equal("labor", labor.Category);
        Assert.Equal("LB1", labor.SourceLineKey);
        Assert.Equal(6m, labor.Quantity);
        Assert.Equal("unit", labor.Unit);
        Assert.Equal(1.75m, labor.UnitCost);
        Assert.Equal(10.50m, labor.LineTotal);
    }

    [Fact]
    public void Run_LaborLineUnknownOperation_ReturnsUnknownOperationError()
    {
        // Arrange
        var element = CreateElement();
        var lines = new List<BomLine> { new LaborBomLine { LineKey = "LB1", OperationCode = "MISSING", Units = 6m } };
        var resolved = CreateResolved(element, CreateSelection(), lines);
        var snapshot = new CatalogueSnapshot { Version = "1" };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(result);
        var error = Assert.Single(errors);
        Assert.Equal(PricingErrorKind.UnknownOperation, error.Kind);
        Assert.Equal("MISSING", error.Subject);
    }

    [Fact]
    public void Run_ChoiceSurchargeSelected_EmitsSurchargeLine()
    {
        // Arrange
        var element = CreateElement();
        var selection = CreateSelection(choices: new Dictionary<string, string> { ["COLOR"] = "RED" });
        var resolved = CreateResolved(element, selection, []);
        var snapshot = new CatalogueSnapshot { Version = "1", ChoiceSurcharges = [new ChoiceSurcharge("RED", null, 7.50m)] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        var surcharge = Assert.Single(result);
        Assert.Equal(BreakdownStage.Surcharges, surcharge.Stage);
        Assert.Equal("choice-surcharge", surcharge.Category);
        Assert.Null(surcharge.SourceLineKey);
        Assert.Equal("pc", surcharge.Unit);
        Assert.Equal(7.50m, surcharge.LineTotal);
    }

    [Fact]
    public void Run_ChoiceSurchargeElementCodeMatches_EmitsSurchargeLine()
    {
        // Arrange
        var element = CreateElement("SEAT");
        var selection = CreateSelection("SEAT", new Dictionary<string, string> { ["COLOR"] = "RED" });
        var resolved = CreateResolved(element, selection, []);
        var snapshot = new CatalogueSnapshot { Version = "1", ChoiceSurcharges = [new ChoiceSurcharge("RED", "SEAT", 7.50m)] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        var surcharge = Assert.Single(result);
        Assert.Equal("choice-surcharge", surcharge.Category);
        Assert.Equal(7.50m, surcharge.LineTotal);
    }

    [Fact]
    public void Run_ChoiceSurchargeElementCodeMismatch_IsNotApplied()
    {
        // Arrange
        var element = CreateElement("SEAT");
        var selection = CreateSelection("SEAT", new Dictionary<string, string> { ["COLOR"] = "RED" });
        var resolved = CreateResolved(element, selection, []);
        var snapshot = new CatalogueSnapshot { Version = "1", ChoiceSurcharges = [new ChoiceSurcharge("RED", "OTHER_ELEMENT", 7.50m)] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        Assert.Empty(result);
    }

    [Fact]
    public void Run_CombinationPriceRuleAllRequiredSelectionsMatch_EmitsAdjustmentLine()
    {
        // Arrange
        var element = CreateElement();
        var selection = CreateSelection(choices: new Dictionary<string, string> { ["COLOR"] = "RED", ["SIZE"] = "LARGE" });
        var resolved = CreateResolved(element, selection, []);
        var rule = new CombinationPriceRule([new SelectionKey("COLOR", "RED"), new SelectionKey("SIZE", "LARGE")], -15.00m);
        var snapshot = new CatalogueSnapshot { Version = "1", CombinationPriceRules = [rule] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        var combination = Assert.Single(result);
        Assert.Equal(BreakdownStage.Surcharges, combination.Stage);
        Assert.Equal("combination", combination.Category);
        Assert.Null(combination.SourceLineKey);
        Assert.Equal(-15.00m, combination.LineTotal);
    }

    [Fact]
    public void Run_CombinationPriceRulePartialMatch_IsNotApplied()
    {
        // Arrange
        var element = CreateElement();
        var selection = CreateSelection(choices: new Dictionary<string, string> { ["COLOR"] = "RED" });
        var resolved = CreateResolved(element, selection, []);
        var rule = new CombinationPriceRule([new SelectionKey("COLOR", "RED"), new SelectionKey("SIZE", "LARGE")], -15.00m);
        var snapshot = new CatalogueSnapshot { Version = "1", CombinationPriceRules = [rule] };

        // Act
        var (result, errors) = CostStages.Run(resolved, snapshot, Rounding);

        // Assert
        Assert.Empty(errors);
        Assert.Empty(result);
    }
}
