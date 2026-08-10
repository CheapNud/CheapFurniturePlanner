using CheapFurniturePlanner.Domain.Export;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Export;

public class CatalogueFlattenerTests
{
    private static CatalogueSnapshot Snapshot() => DemoWorld.Load();

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
        // representative configuration the flattener uses (first visible choice values +
        // first colour of the first fabric group mapping to that price group) and assert equality.
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
                choiceSelections[option.OptionDefinitionCode] = option.Values[0].OptionChoiceCode;
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
}
