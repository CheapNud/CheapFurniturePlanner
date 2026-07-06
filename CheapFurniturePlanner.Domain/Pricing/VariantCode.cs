using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;

namespace CheapFurniturePlanner.Domain.Pricing;

public static class VariantCode
{
    public const string MaterialDefCode = "__MATERIAL__";

    // Element code + '-' + each BOM-significant selection "DEF:CHOICE", segments ordered ordinally.
    public static string From(Element element, ElementSelection selection) => From(element, selection, null);

    // Overload that also bakes the resolved material type (fabric/leather/thick-leather) into the
    // variant string as a synthetic __MATERIAL__ segment. Color is intentionally never part of this.
    public static string From(Element element, ElementSelection selection, string? materialTypeCode)
    {
        List<string> segments = element.Options.OfType<ChoiceOption>()
            .Where(po => po.AffectsBom)
            .Select(po => po.OptionDefinitionCode)
            .Where(defCode => selection.ChoiceSelections.ContainsKey(defCode))
            .Select(defCode => $"{defCode}:{selection.ChoiceSelections[defCode]}")
            .ToList();
        if (!string.IsNullOrEmpty(materialTypeCode))
        {
            segments.Add($"{MaterialDefCode}:{materialTypeCode}");
        }
        segments.Sort(StringComparer.Ordinal);
        return string.Join('-', [element.Code, .. segments]);
    }
}
