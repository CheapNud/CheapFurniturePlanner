using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Options;

namespace CheapFurniturePlanner.Domain.Pricing.Engine;

internal static class ResolveStage
{
    // returns errors OR resolved elements; applies: model/element lookup, required+visibility validation,
    // fabric colour -> group -> price group (kind from selected material option context), BOM condition filter, substitution rules.
    internal static (List<ResolvedElement> Resolved, List<PricingError> Errors) Run(PricingRequest request)
    {
        List<PricingError> errors = [];
        List<ResolvedElement> resolved = [];

        // Rule 6: market must exist in the snapshot.
        var marketCode = request.Context.Market.Code;
        if (!request.Snapshot.Markets.Any(m => m.Code == marketCode))
        {
            errors.Add(new PricingError(PricingErrorKind.UnknownMarket, marketCode));
        }

        // Rule 1: model must exist.
        var model = request.Snapshot.Models.FirstOrDefault(m => m.Code == request.Configuration.ModelCode);
        if (model is null)
        {
            errors.Add(new PricingError(PricingErrorKind.UnknownModel, request.Configuration.ModelCode));
            return (resolved, errors);
        }

        foreach (var selection in request.Configuration.Selections)
        {
            // Rule 2: element must exist on the model.
            var element = model.Elements.FirstOrDefault(e => e.Code == selection.ElementCode);
            if (element is null)
            {
                errors.Add(new PricingError(PricingErrorKind.UnknownElement, selection.ElementCode));
                continue;
            }

            List<PricingError> elementErrors = [];

            ValidateOptions(element, selection, elementErrors);
            var priceGroup = ResolveFabricPriceGroup(request.Snapshot, element, selection, elementErrors);

            if (elementErrors.Count > 0)
            {
                errors.AddRange(elementErrors);
                continue;
            }

            // Rule 7: an element with no BOM sections at all has nothing to cost.
            if (element.Bom.Sections.Count == 0)
            {
                errors.Add(new PricingError(PricingErrorKind.MissingBomSection, element.Code));
                continue;
            }

            var effectiveLines = BuildEffectiveLines(element, selection);
            var variantCode = VariantCode.From(element, selection);
            resolved.Add(new ResolvedElement(element, selection, variantCode, priceGroup, effectiveLines));
        }

        return (resolved, errors);
    }

    // Rule 3: visibility + required-choice completeness + selection validity.
    private static void ValidateOptions(Element element, ElementSelection selection, List<PricingError> errors)
    {
        foreach (var option in element.Options)
        {
            var isVisible = IsVisible(option, selection.ChoiceSelections);

            if (option is ChoiceOption choiceOption && isVisible && choiceOption.Required
                && !selection.ChoiceSelections.ContainsKey(choiceOption.OptionDefinitionCode))
            {
                errors.Add(new PricingError(PricingErrorKind.IncompleteConfiguration, $"{element.Code}:{choiceOption.OptionDefinitionCode}"));
            }
        }

        foreach (var (defCode, choiceCode) in selection.ChoiceSelections)
        {
            var choiceOption = element.Options.OfType<ChoiceOption>().FirstOrDefault(o => o.OptionDefinitionCode == defCode);
            if (choiceOption is null || !choiceOption.Values.Any(v => v.OptionChoiceCode == choiceCode))
            {
                errors.Add(new PricingError(PricingErrorKind.UnknownOptionSelection, $"{element.Code}:{defCode}={choiceCode}"));
                continue;
            }

            if (!IsVisible(choiceOption, selection.ChoiceSelections))
            {
                errors.Add(new PricingError(PricingErrorKind.SelectionViolatesVisibility, $"{element.Code}:{defCode}={choiceCode}"));
            }
        }
    }

    private static bool IsVisible(ProductOption option, IReadOnlyDictionary<string, string> choiceSelections) =>
        option.VisibilityRules.Count == 0
        || option.VisibilityRules.Any(r => choiceSelections.TryGetValue(r.TriggerOptionDefinitionCode, out var chosen) && chosen == r.TriggerChoiceCode);

    // Rule 4: fabric colour -> group -> price group.
    private static PriceGroup ResolveFabricPriceGroup(CatalogueSnapshot snapshot, Element element, ElementSelection selection, List<PricingError> errors)
    {
        var fabricOption = element.Options.OfType<FabricOption>().FirstOrDefault();
        if (fabricOption is null || !IsVisible(fabricOption, selection.ChoiceSelections))
        {
            // Sentinel price group for elements without an active FabricOption: they have no material rate to
            // resolve, but ResolvedElement always carries a PriceGroup, so a neutral zero-rate placeholder is used instead.
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

    // Rule 5: condition-filtered BOM lines (each paired with its section kind) with substitution rules applied.
    private static IReadOnlyList<EffectiveLine> BuildEffectiveLines(Element element, ElementSelection selection)
    {
        var lines = element.Bom.Sections
            .SelectMany(section => section.Lines.Select(line => new EffectiveLine(section.Kind, line)))
            .Where(effective => effective.Line.Condition is null || effective.Line.Condition.IsSatisfiedBy(selection.ChoiceSelections))
            .ToList();

        foreach (var rule in element.Substitutions)
        {
            if (!rule.When.IsSatisfiedBy(selection.ChoiceSelections))
            {
                continue;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                lines[i] = lines[i].Line switch
                {
                    FoamBomLine foam when foam.FoamCode == rule.ReplaceMaterialCode =>
                        lines[i] with { Line = foam with { FoamCode = rule.WithMaterialCode, Quantity = rule.QuantityOverride ?? foam.Quantity } },
                    MiscBomLine misc when misc.MaterialCode == rule.ReplaceMaterialCode =>
                        lines[i] with { Line = misc with { MaterialCode = rule.WithMaterialCode, Quantity = rule.QuantityOverride ?? misc.Quantity } },
                    _ => lines[i]
                };
            }
        }

        return lines;
    }
}
