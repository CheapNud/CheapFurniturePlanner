using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Domain.Production;

// Computes an element's full BOM-significant variant space: every visibility-pruned choice combination
// crossed with every distinct material type, de-duplicated by the composed VariantCode. Lets the
// modellenkamer enumerate and name variants ahead of any actual configuration being placed.
public static class VariantEnumerator
{
    public static IReadOnlyList<VariantDescriptor> Enumerate(Element element, CatalogueSnapshot snapshot)
    {
        // Expand ALL choice options (visibility-respecting) in DisplayIndex order; VariantCode.From
        // filters to AffectsBom internally, so non-BOM selections collapse under de-dup by VariantCode.
        // Assumption: visibility triggers precede their dependents in DisplayIndex order (the natural
        // authoring order the configurator also relies on).
        var choiceOptions = element.Options.OfType<ChoiceOption>().OrderBy(o => o.DisplayIndex).ToList();
        List<Dictionary<string, string>> combos = [];
        Expand(choiceOptions, 0, [], combos);

        var fabricOption = element.Options.OfType<FabricOption>().FirstOrDefault();
        Dictionary<string, VariantDescriptor> byCode = [];
        foreach (var combo in combos)
        {
            foreach (var materialType in MaterialTypesFor(fabricOption, combo, snapshot))
            {
                var selection = new ElementSelection(element.Code, 1, combo, null);
                var variantCode = VariantCode.From(element, selection, materialType);
                if (byCode.ContainsKey(variantCode)) { continue; }
                byCode[variantCode] = new VariantDescriptor(variantCode, BomSignificant(element, combo, materialType), materialType);
            }
        }
        return byCode.Values.OrderBy(d => d.VariantCode, StringComparer.Ordinal).ToList();
    }

    private static void Expand(List<ChoiceOption> options, int index, Dictionary<string, string> partial, List<Dictionary<string, string>> sink)
    {
        if (index == options.Count) { sink.Add(new Dictionary<string, string>(partial)); return; }
        var option = options[index];
        if (OptionVisibility.IsVisible(option, partial))
        {
            foreach (var choiceValue in option.Values)
            {
                partial[option.OptionDefinitionCode] = choiceValue.OptionChoiceCode;
                Expand(options, index + 1, partial, sink);
                partial.Remove(option.OptionDefinitionCode);
            }
        }
        else
        {
            Expand(options, index + 1, partial, sink); // hidden option contributes no segment
        }
    }

    // Distinct material types available to this combo: only when the fabric option is present AND visible.
    private static IEnumerable<string?> MaterialTypesFor(FabricOption? fabricOption, IReadOnlyDictionary<string, string> combo, CatalogueSnapshot snapshot)
    {
        if (fabricOption is null || !OptionVisibility.IsVisible(fabricOption, combo))
        {
            return [null];
        }
        var types = fabricOption.FabricGroupCodes
            .Select(code => snapshot.FabricGroups.FirstOrDefault(g => g.Code == code))
            .Where(g => g is not null)
            .Select(g => snapshot.PriceGroups.FirstOrDefault(pg => pg.Code == g!.PriceGroupCode))
            .Where(pg => pg is not null)
            .Select(pg => MaterialResolution.MaterialTypeCode(pg!))
            .Where(t => t is not null)
            .Distinct()
            .ToList();
        return types.Count == 0 ? [null] : types;
    }

    private static IReadOnlyDictionary<string, string> BomSignificant(Element element, IReadOnlyDictionary<string, string> combo, string? materialType)
    {
        Dictionary<string, string> significant = element.Options.OfType<ChoiceOption>()
            .Where(o => o.AffectsBom && combo.ContainsKey(o.OptionDefinitionCode))
            .ToDictionary(o => o.OptionDefinitionCode, o => combo[o.OptionDefinitionCode]);
        if (!string.IsNullOrEmpty(materialType)) { significant[VariantCode.MaterialDefCode] = materialType; }
        return significant;
    }
}
