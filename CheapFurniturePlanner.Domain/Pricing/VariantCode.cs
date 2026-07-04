using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;

namespace CheapFurniturePlanner.Domain.Pricing;

public static class VariantCode
{
    // Element code + '-' + each BOM-significant selection "DEF:CHOICE", segments ordered by OptionDefinitionCode ordinal.
    public static string From(Element element, ElementSelection selection)
    {
        var significantDefs = element.Options.OfType<ChoiceOption>()
            .Where(po => po.AffectsBom)
            .Select(po => po.OptionDefinitionCode)
            .OrderBy(defCode => defCode, StringComparer.Ordinal);
        var segments = significantDefs
            .Where(defCode => selection.ChoiceSelections.ContainsKey(defCode))
            .Select(defCode => $"{defCode}:{selection.ChoiceSelections[defCode]}");
        return string.Join('-', [element.Code, .. segments]);
    }
}
