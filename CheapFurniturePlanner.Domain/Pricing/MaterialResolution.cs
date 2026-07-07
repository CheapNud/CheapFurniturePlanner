using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Options;

namespace CheapFurniturePlanner.Domain.Pricing;

internal static class MaterialResolution
{
    // Fabric colour -> group -> price group. Returns a neutral sentinel PriceGroup (empty Code) when the element
    // has no active FabricOption or the colour is unresolvable; appends the matching PricingError in those cases.
    internal static PriceGroup ResolveFabricPriceGroup(CatalogueSnapshot snapshot, Element element, ElementSelection selection, List<PricingError> errors)
    {
        var fabricOption = element.Options.OfType<FabricOption>().FirstOrDefault();
        if (fabricOption is null || !OptionVisibility.IsVisible(fabricOption, selection.ChoiceSelections))
        {
            return new PriceGroup { Code = "", Kind = MaterialKind.Fabric, RatePerMeter = 0m };
        }
        if (selection.FabricColorCode is null)
        {
            errors.Add(new PricingError(PricingErrorKind.IncompleteConfiguration, $"{element.Code}:{fabricOption.OptionDefinitionCode}"));
            return new PriceGroup { Code = "", Kind = MaterialKind.Fabric, RatePerMeter = 0m };
        }
        var group = fabricOption.FabricGroupCodes
            .Select(code => snapshot.FabricGroups.FirstOrDefault(g => g.Code == code))
            .FirstOrDefault(g => g is not null && g.Colors.Any(c => c.Code == selection.FabricColorCode));
        if (group is null)
        {
            errors.Add(new PricingError(PricingErrorKind.UnknownFabricColor, $"{element.Code}:{selection.FabricColorCode}"));
            return new PriceGroup { Code = "", Kind = MaterialKind.Fabric, RatePerMeter = 0m };
        }
        var priceGroup = snapshot.PriceGroups.FirstOrDefault(pg => pg.Code == group.PriceGroupCode);
        if (priceGroup is null)
        {
            errors.Add(new PricingError(PricingErrorKind.NoPriceGroupForMaterialKind, $"{element.Code}:{group.PriceGroupCode}"));
            return new PriceGroup { Code = "", Kind = MaterialKind.Fabric, RatePerMeter = 0m };
        }
        return priceGroup;
    }

    // Material type is BOM-significant (fabric vs leather vs thick-leather); colour is not. A sentinel price group
    // (empty Code) carries no material. Mirrors the exact derivation ResolveStage used inline.
    internal static string? MaterialTypeCode(PriceGroup priceGroup) =>
        priceGroup.Code.Length == 0 ? null : priceGroup.MaterialTypeCode ?? priceGroup.Kind.ToString();
}
