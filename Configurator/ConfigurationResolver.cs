using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Configurator;

public static class ConfigurationResolver
{
    public static bool IsVisible(ProductOption option, IReadOnlyDictionary<string, string> selections) =>
        option.VisibilityRules.Count == 0
        || option.VisibilityRules.Any(r => selections.TryGetValue(r.TriggerOptionDefinitionCode, out var chosen) && chosen == r.TriggerChoiceCode);

    public static IReadOnlyList<ChoiceOption> VisibleOptions(Element element, IReadOnlyDictionary<string, string> selections) =>
        element.Options.OfType<ChoiceOption>().Where(o => IsVisible(o, selections)).OrderBy(o => o.DisplayIndex).ToList();

    public static Dictionary<string, string> DefaultSelections(Element element)
    {
        Dictionary<string, string> selections = [];
        // Seed each option's default, then keep only those that end up visible (a revealed sub-option gets its default too, iteratively).
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var option in element.Options.OfType<ChoiceOption>())
            {
                if (!IsVisible(option, selections) || selections.ContainsKey(option.OptionDefinitionCode))
                {
                    continue;
                }
                var def = option.Values.FirstOrDefault(v => v.IsDefault) ?? option.Values.OrderBy(v => v.DisplayIndex).FirstOrDefault();
                if (def is not null)
                {
                    selections[option.OptionDefinitionCode] = def.OptionChoiceCode;
                    changed = true;
                }
            }
        }
        return selections;
    }

    public static IReadOnlyList<FabricGroup> FabricGroupsFor(Element element, CatalogueSnapshot snapshot)
    {
        var option = element.Options.OfType<FabricOption>().FirstOrDefault();
        if (option is null)
        {
            return [];
        }
        return option.FabricGroupCodes
            .Select(code => snapshot.FabricGroups.FirstOrDefault(g => g.Code == code))
            .Where(g => g is not null)
            .Select(g => g!)
            .ToList();
    }

    public static string? DefaultFabricColorCode(Element element, CatalogueSnapshot snapshot) =>
        FabricGroupsFor(element, snapshot).SelectMany(g => g.Colors).FirstOrDefault()?.Code;

    public static string? FindModelCode(CatalogueSnapshot snapshot, string elementCode) =>
        snapshot.Models.FirstOrDefault(m => m.Elements.Any(e => e.Code == elementCode))?.Code;

    public static string? MaterialTypeLabel(FabricGroup group, CatalogueSnapshot snapshot)
    {
        var pg = snapshot.PriceGroups.FirstOrDefault(p => p.Code == group.PriceGroupCode);
        return pg?.MaterialTypeCode ?? pg?.Kind.ToString();
    }

    // The published price group behind a chosen fabric colour: the colour's fabric group among the
    // element's fabric option groups -> its PriceGroupCode. Null when no colour or no match. App-side
    // mirror of the engine's internal material resolution, for discount-rule lookups.
    public static string? ResolvedPriceGroupCode(Element element, CatalogueSnapshot snapshot, string? fabricColorCode)
    {
        if (fabricColorCode is null) { return null; }
        var groupCodes = element.Options.OfType<FabricOption>().SelectMany(o => o.FabricGroupCodes).ToHashSet();
        return snapshot.FabricGroups
            .FirstOrDefault(g => groupCodes.Contains(g.Code) && g.Colors.Any(c => c.Code == fabricColorCode))
            ?.PriceGroupCode;
    }
}
