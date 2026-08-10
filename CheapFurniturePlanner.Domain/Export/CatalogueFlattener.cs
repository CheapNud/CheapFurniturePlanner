using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Domain.Export;

// Flattens a published snapshot to one row per element x fabric price group x market - the
// price-list grain of this trade (the price group IS the pricing axis; never per-colour).
// Prices come from the SAME engine every order uses, priced at seller multiplier 1 with the
// element's representative configuration: first value of each visible choice option in
// DisplayIndex order, plus the first colour of the first fabric group mapping to the price
// group. An unpriceable combination yields NO row - omitted means not orderable, never zero.
public static class CatalogueFlattener
{
    public static IReadOnlyList<CatalogueRow> Flatten(CatalogueSnapshot snapshot)
    {
        List<CatalogueRow> rows = [];
        foreach (var model in snapshot.Models)
        {
            foreach (var element in model.Elements)
            {
                var defaults = DefaultSelections(element);
                foreach (var market in snapshot.Markets)
                {
                    foreach (var (priceGroupCode, fabricColorCode) in PriceGroupRepresentatives(element, snapshot, defaults))
                    {
                        var configuration = new ProductConfiguration(model.Code,
                            [new ElementSelection(element.Code, 1, defaults, fabricColorCode)]);
                        var result = PricingEngine.Calculate(new PricingRequest(snapshot, configuration,
                            new PricingContext(market, 1m)));
                        if (!result.IsSuccess) { continue; }
                        rows.Add(new CatalogueRow(snapshot.Version, snapshot.ContentHash,
                            model.Code, model.Name, model.CollectionCode,
                            element.Code, element.Name, priceGroupCode, market.Code,
                            result.Breakdown!.Elements[0].ElementTotal));
                    }
                }
            }
        }
        return rows
            .OrderBy(r => r.ModelCode, StringComparer.Ordinal)
            .ThenBy(r => r.ElementCode, StringComparer.Ordinal)
            .ThenBy(r => r.PriceGroupCode, StringComparer.Ordinal)
            .ThenBy(r => r.MarketCode, StringComparer.Ordinal)
            .ToList();
    }

    // First value of each VISIBLE choice option, walked in DisplayIndex order so visibility
    // triggers resolve before their dependents (the VariantEnumerator assumption).
    private static Dictionary<string, string> DefaultSelections(Element element)
    {
        Dictionary<string, string> selections = [];
        foreach (var option in element.Options.OfType<ChoiceOption>().OrderBy(o => o.DisplayIndex))
        {
            if (OptionVisibility.IsVisible(option, selections) && option.Values.Count > 0)
            {
                selections[option.OptionDefinitionCode] = option.Values[0].OptionChoiceCode;
            }
        }
        return selections;
    }

    // Distinct price groups reachable through the element's fabric option, each with a
    // representative colour; a fabric-less element (no fabric option, or one hidden under the
    // default selections) prices once with an empty group code.
    private static IEnumerable<(string PriceGroupCode, string? FabricColorCode)> PriceGroupRepresentatives(
        Element element, CatalogueSnapshot snapshot, IReadOnlyDictionary<string, string> defaultSelections)
    {
        var fabricOption = element.Options.OfType<FabricOption>().FirstOrDefault();
        if (fabricOption is null || !OptionVisibility.IsVisible(fabricOption, defaultSelections))
        {
            yield return ("", null);
            yield break;
        }
        HashSet<string> seen = [];
        foreach (var groupCode in fabricOption.FabricGroupCodes)
        {
            var fabricGroup = snapshot.FabricGroups.FirstOrDefault(g => g.Code == groupCode);
            var representativeColor = fabricGroup?.Colors.FirstOrDefault();
            if (fabricGroup is null || representativeColor is null) { continue; }
            if (!seen.Add(fabricGroup.PriceGroupCode)) { continue; }
            yield return (fabricGroup.PriceGroupCode, representativeColor.Code);
        }
    }
}
