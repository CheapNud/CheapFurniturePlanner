using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;

namespace CheapFurniturePlanner.Domain.Pricing;

public static class VariantCode
{
    public const string MaterialDefCode = "__MATERIAL__";

    // '-' separates segments below, ':' separates each segment's def code from its choice code - so
    // neither may appear inside an element code, option def code, or choice code, or a variant string
    // could no longer be split back into its parts. Every code-accepting authoring seam
    // (ElementAuthoringService, OptionAuthoringService) and the publish validator
    // (CataloguePublishService) funnel their codes through FindReservedSeparator below - the single
    // guard for this rule (see TODO.md 2026-07-05 [audit]).
    public static readonly char[] ReservedSeparators = ['-', ':'];

    // Returns the first reserved separator found in code, or null if it's clean. Non-throwing so both
    // an authoring service (throw immediately) and the publish validator (collect as an error string)
    // can share the same check.
    public static char? FindReservedSeparator(string code)
    {
        var index = code.IndexOfAny(ReservedSeparators);
        return index >= 0 ? code[index] : null;
    }

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
