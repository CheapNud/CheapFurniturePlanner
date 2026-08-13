using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Export;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Export;

public class CatalogueFlattenerTests
{
    private static readonly RoundingPolicy Rounding =
        new(2, 2, MidpointRounding.AwayFromZero, RoundStage.Line | RoundStage.Subtotal | RoundStage.Final);

    private static CatalogueSnapshot Snapshot() => DemoWorld.Load();

    // Single Labor BOM line element, mirroring PricingEngineTests.CreateElement - minimal but priceable.
    private static Element LaborOnlyElement(string code, List<ProductOption> options) => new()
    {
        Code = code,
        Name = code,
        Options = options,
        Bom = new BomDocument
        {
            Sections =
            [
                new BomSection
                {
                    Kind = BomSectionKind.Labor,
                    Lines = [new LaborBomLine { LineKey = "LB1", OperationCode = "OP1", Units = 10m }]
                }
            ]
        }
    };

    [Fact]
    public void Flatten_StampsVersionAndHashOnEveryRow()
    {
        var snapshot = Snapshot();
        var rows = CatalogueFlattener.Flatten(snapshot);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(snapshot.Version, r.CatalogueVersion));
        Assert.All(rows, r => Assert.Equal(snapshot.ContentHash, r.ContentHash));
    }

    [Fact]
    public void Flatten_GrainIsElementPriceGroupMarket_NoDuplicates()
    {
        var rows = CatalogueFlattener.Flatten(Snapshot());
        var keys = rows.Select(r => (r.ModelCode, r.ElementCode, r.PriceGroupCode, r.MarketCode)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        // every market in the snapshot appears (DemoWorld has >= 1 market)
        Assert.Equal(Snapshot().Markets.Select(m => m.Code).OrderBy(c => c),
            rows.Select(r => r.MarketCode).Distinct().OrderBy(c => c));
    }

    [Fact]
    public void Flatten_PriceMatchesEngineForRepresentativeConfiguration()
    {
        // pick one row with a non-empty PriceGroupCode; re-run the engine with the SAME
        // representative configuration the flattener uses (authored default of each visible choice
        // option - IsDefault, falling back to lowest DisplayIndex - plus first colour of the first
        // fabric group mapping to that price group) and assert equality.
        // This is the "documents can never disagree with the engine" fact.
        var snapshot = Snapshot();
        var rows = CatalogueFlattener.Flatten(snapshot);
        var row = rows.First(r => !string.IsNullOrEmpty(r.PriceGroupCode));

        var model = snapshot.Models.Single(m => m.Code == row.ModelCode);
        var element = model.Elements.Single(e => e.Code == row.ElementCode);

        Dictionary<string, string> choiceSelections = [];
        foreach (var option in element.Options.OfType<ChoiceOption>().OrderBy(o => o.DisplayIndex))
        {
            if (OptionVisibility.IsVisible(option, choiceSelections) && option.Values.Count > 0)
            {
                var defaultValue = option.Values.FirstOrDefault(v => v.IsDefault) ?? option.Values.OrderBy(v => v.DisplayIndex).First();
                choiceSelections[option.OptionDefinitionCode] = defaultValue.OptionChoiceCode;
            }
        }

        var fabricOption = element.Options.OfType<FabricOption>().Single();
        var fabricGroup = fabricOption.FabricGroupCodes
            .Select(code => snapshot.FabricGroups.Single(g => g.Code == code))
            .First(g => g.PriceGroupCode == row.PriceGroupCode);
        var fabricColorCode = fabricGroup.Colors[0].Code;

        var market = snapshot.Markets.Single(m => m.Code == row.MarketCode);
        var configuration = new ProductConfiguration(row.ModelCode,
            [new ElementSelection(row.ElementCode, 1, choiceSelections, fabricColorCode)]);
        var result = PricingEngine.Calculate(new PricingRequest(snapshot, configuration, new PricingContext(market, 1m)));

        Assert.True(result.IsSuccess);
        Assert.Equal(row.Price, result.Breakdown!.Elements[0].ElementTotal);
    }

    [Fact]
    public void Flatten_OmitsUnpriceableCombinations_NeverZeroRows()
    {
        var rows = CatalogueFlattener.Flatten(Snapshot());
        Assert.DoesNotContain(rows, r => r.Price == 0m);

        // mutate a copy of the snapshot to break one price group's pricing input (remove the
        // fabric group that maps to it, on every element that references it) => rows for that
        // group disappear, total row count shrinks, no zero-price row appears.
        var mutated = Snapshot();
        mutated.FabricGroups.RemoveAll(g => g.Code == "HIDE");
        var mutatedRows = CatalogueFlattener.Flatten(mutated);

        Assert.True(mutatedRows.Count < rows.Count);
        Assert.DoesNotContain(mutatedRows, r => r.PriceGroupCode == "PGL");
        Assert.DoesNotContain(mutatedRows, r => r.Price == 0m);
    }

    [Fact]
    public void Flatten_DeterministicOrdering()
    {
        var first = CatalogueFlattener.Flatten(Snapshot());
        var second = CatalogueFlattener.Flatten(Snapshot());
        Assert.Equal(first, second); // records: value equality, ordinal sort inside Flatten
    }

    [Fact]
    public void Flatten_HonorsAuthoredDefaultValue_NotFirstInList()
    {
        // The option's SECOND value (BLUE) is marked IsDefault and carries a ChoiceSurcharge that the
        // FIRST value (RED) does not - a flattener that still picks Values[0] (raw list order) prices
        // without the surcharge, while the correct representative configuration (matching
        // ConfigurationResolver's own default rule) must price with it.
        var element = LaborOnlyElement("SEAT",
        [
            new ChoiceOption
            {
                OptionDefinitionCode = "COLOR",
                DisplayIndex = 0,
                Required = false,
                Values =
                [
                    new ProductOptionValue { OptionChoiceCode = "RED", DisplayIndex = 0, IsDefault = false },
                    new ProductOptionValue { OptionChoiceCode = "BLUE", DisplayIndex = 1, IsDefault = true },
                ],
            },
        ]);
        var market = new MarketParameters("EU", TransportRatePerUnit: 3.00m, FixedCostPercent: 10m, MarkupSteps: [], Rounding: Rounding);
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            ContentHash = "HASH1",
            Models = [new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] }],
            Operations = [new Operation("OP1", "Sew", 5.00m)],
            Markets = [market],
            ChoiceSurcharges = [new ChoiceSurcharge("BLUE", null, 18.00m)],
        };

        var rows = CatalogueFlattener.Flatten(snapshot);
        var row = Assert.Single(rows);

        // Independently price the engine's own default (BLUE, per ConfigurationResolver's IsDefault rule).
        var defaultConfiguration = new ProductConfiguration("SOFA",
            [new ElementSelection(element.Code, 1, new Dictionary<string, string> { ["COLOR"] = "BLUE" }, null)]);
        var expected = PricingEngine.Calculate(new PricingRequest(snapshot, defaultConfiguration, new PricingContext(market, 1m)));

        Assert.True(expected.IsSuccess);
        Assert.Equal(expected.Breakdown!.Elements[0].ElementTotal, row.Price);
    }

    // Export 3: documents CatalogueFlattener.DefaultSelections' own stated assumption ("visibility
    // triggers resolve before their dependents" - mirrors VariantEnumerator's identical comment).
    // HEAD (dependent, DisplayIndex 0) is gated on MECH (trigger, DisplayIndex 1) - BACKWARDS from
    // that assumption. The single DisplayIndex-order pass evaluates HEAD's visibility before MECH's
    // default has been recorded, so HEAD reads as hidden and its own default (with a surcharge) is
    // silently excluded from the representative configuration - even though a full, order-independent
    // evaluation (MECH's actual default satisfies HEAD's trigger) would include it. Pins TODAY's
    // behavior; the resolver's fixed-point loop (re-evaluate visibility until it stops changing) is
    // the named upgrade path if this ever needs to be order-independent, not a fix here.
    [Fact]
    public void Flatten_DefaultWalk_AssumesVisibilityTriggersPrecedeDependentsInDisplayIndexOrder()
    {
        var element = LaborOnlyElement("SEAT",
        [
            new ChoiceOption
            {
                OptionDefinitionCode = "HEAD",
                DisplayIndex = 0,
                Required = false,
                VisibilityRules = [new VisibilityRule("MECH", "RECLINE", "HEAD")],
                Values = [new ProductOptionValue { OptionChoiceCode = "HS1", DisplayIndex = 0, IsDefault = true }],
            },
            new ChoiceOption
            {
                OptionDefinitionCode = "MECH",
                DisplayIndex = 1,
                Required = false,
                Values = [new ProductOptionValue { OptionChoiceCode = "RECLINE", DisplayIndex = 0, IsDefault = true }],
            },
        ]);
        var market = new MarketParameters("EU", TransportRatePerUnit: 3.00m, FixedCostPercent: 10m, MarkupSteps: [], Rounding: Rounding);
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            ContentHash = "HASH1",
            Models = [new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] }],
            Operations = [new Operation("OP1", "Sew", 5.00m)],
            Markets = [market],
            ChoiceSurcharges = [new ChoiceSurcharge("HS1", null, 18.00m)],
        };

        var rows = CatalogueFlattener.Flatten(snapshot);
        var row = Assert.Single(rows);

        // What the flattener actually priced: HEAD excluded (its visibility read false mid-walk).
        var withoutHead = PricingEngine.Calculate(new PricingRequest(snapshot,
            new ProductConfiguration("SOFA", [new ElementSelection(element.Code, 1, new Dictionary<string, string> { ["MECH"] = "RECLINE" }, null)]),
            new PricingContext(market, 1m)));
        // What a full, order-independent evaluation would price: both defaults applied, HS1's surcharge included.
        var withHead = PricingEngine.Calculate(new PricingRequest(snapshot,
            new ProductConfiguration("SOFA", [new ElementSelection(element.Code, 1, new Dictionary<string, string> { ["MECH"] = "RECLINE", ["HEAD"] = "HS1" }, null)]),
            new PricingContext(market, 1m)));

        Assert.True(withoutHead.IsSuccess);
        Assert.True(withHead.IsSuccess);
        Assert.NotEqual(withHead.Breakdown!.Elements[0].ElementTotal, withoutHead.Breakdown!.Elements[0].ElementTotal);
        Assert.Equal(withoutHead.Breakdown!.Elements[0].ElementTotal, row.Price);
    }

    [Fact]
    public void Flatten_ElementWithoutFabricOption_YieldsOneEmptyPriceGroupRowPerMarket()
    {
        var element = LaborOnlyElement("SEAT", []);
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            Models = [new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] }],
            Operations = [new Operation("OP1", "Sew", 5.00m)],
            Markets =
            [
                new MarketParameters("EU", 3.00m, 10m, [], Rounding),
                new MarketParameters("US", 3.00m, 10m, [], Rounding),
            ],
        };

        var rows = CatalogueFlattener.Flatten(snapshot);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("", r.PriceGroupCode));
    }

    [Fact]
    public void Flatten_HiddenFabricOption_YieldsOneEmptyPriceGroupRow()
    {
        // FabricOption gated behind a trigger that never appears in the element's default
        // selections - hidden under the defaults, so it prices once with an empty group code.
        var element = LaborOnlyElement("SEAT",
        [
            new FabricOption
            {
                OptionDefinitionCode = "FABRIC",
                DisplayIndex = 0,
                VisibilityRules = [new VisibilityRule("UPGRADE", "YES", "FABRIC")],
                FabricGroupCodes = ["FG1"],
            },
        ]);
        var snapshot = new CatalogueSnapshot
        {
            Version = "1",
            Models = [new FurnitureModel { Code = "SOFA", Name = "Sofa", Elements = [element] }],
            Operations = [new Operation("OP1", "Sew", 5.00m)],
            Markets = [new MarketParameters("EU", 3.00m, 10m, [], Rounding)],
        };

        var rows = CatalogueFlattener.Flatten(snapshot);

        var row = Assert.Single(rows);
        Assert.Equal("", row.PriceGroupCode);
    }
}
