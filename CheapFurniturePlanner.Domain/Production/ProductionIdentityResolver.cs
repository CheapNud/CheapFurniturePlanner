using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Domain.Production;

public static class ProductionIdentityResolver
{
    public static IReadOnlyList<ProductionIdentity> Resolve(
        CatalogueSnapshot snapshot,
        ProductConfiguration configuration,
        IReadOnlyDictionary<string, string> suggestionsByVariant,
        TradeItemState modelState)
    {
        List<ProductionIdentity> identities = [];
        var model = snapshot.Models.FirstOrDefault(m => m.Code == configuration.ModelCode);
        if (model is null)
        {
            return identities;
        }

        foreach (var selection in configuration.Selections)
        {
            var element = model.Elements.FirstOrDefault(e => e.Code == selection.ElementCode);
            if (element is null)
            {
                continue;
            }

            List<PricingError> ignored = [];
            var priceGroup = MaterialResolution.ResolveFabricPriceGroup(snapshot, element, selection, ignored);
            var materialTypeCode = MaterialResolution.MaterialTypeCode(priceGroup);
            var variantCode = VariantCode.From(element, selection, materialTypeCode);

            var hasSuggestion = suggestionsByVariant.TryGetValue(variantCode, out var suggested) && !string.IsNullOrEmpty(suggested);
            var status = StatusFor(hasSuggestion, modelState);
            var effectiveCode = hasSuggestion ? suggested! : variantCode;
            var isExportable = modelState == TradeItemState.Active;

            var bomSignificant = BomSignificantSelections(element, selection, materialTypeCode);

            identities.Add(new ProductionIdentity(
                model.Code, element.Code, variantCode, hasSuggestion ? suggested : null,
                effectiveCode, status, isExportable, materialTypeCode, bomSignificant));
        }
        return identities;
    }

    // The single three-state rule: no suggestion => Composed; a suggestion under a Draft model =>
    // Provisional; a suggestion under Active/Discontinued => Released. Shared by Resolve() and by any
    // caller that needs to derive a row's status without a full re-resolve.
    public static ProductionCodeStatus StatusFor(bool hasSuggestion, TradeItemState modelState) =>
        !hasSuggestion
            ? ProductionCodeStatus.Composed
            : modelState == TradeItemState.Draft ? ProductionCodeStatus.Provisional : ProductionCodeStatus.Released;

    // The BOM-significant subset that defines the variant: AffectsBom choice selections + the synthetic material type.
    private static IReadOnlyDictionary<string, string> BomSignificantSelections(
        Element element, ElementSelection selection, string? materialTypeCode)
    {
        Dictionary<string, string> significant = element.Options
            .OfType<CheapFurniturePlanner.Domain.Options.ChoiceOption>()
            .Where(po => po.AffectsBom && selection.ChoiceSelections.ContainsKey(po.OptionDefinitionCode))
            .ToDictionary(po => po.OptionDefinitionCode, po => selection.ChoiceSelections[po.OptionDefinitionCode]);
        if (!string.IsNullOrEmpty(materialTypeCode))
        {
            significant[VariantCode.MaterialDefCode] = materialTypeCode;
        }
        return significant;
    }
}
