using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Masters;

namespace CheapFurniturePlanner.Domain.Pricing.Engine;

// Emits BreakdownLines for stages Materials/Fabric/Labor/Surcharges for one ResolvedElement.
// Frame/Foam/Cotton/Misc land in Materials (frame's attached FixedSurcharge + SprayPrice are
// still Materials-stage lines - they are categorized "surcharge"/"spray" but computed alongside
// the frame body cost, not part of the dedicated Surcharges stage below).
internal static class CostStages
{
    internal static (List<BreakdownLine> Lines, List<PricingError> Errors) Run(ResolvedElement resolved, CatalogueSnapshot snapshot, RoundingPolicy rounding)
    {
        List<BreakdownLine> lines = [];
        List<PricingError> errors = [];

        foreach (var line in resolved.EffectiveLines)
        {
            switch (line)
            {
                case FrameBomLine frame:
                    RunFrame(frame, snapshot, rounding, lines, errors);
                    break;
                case FoamBomLine foam:
                    RunFoam(foam, snapshot, rounding, lines, errors);
                    break;
                case CottonBomLine cotton:
                    RunCotton(cotton, snapshot, rounding, lines, errors);
                    break;
                case MiscBomLine misc:
                    RunMisc(misc, snapshot, rounding, lines, errors);
                    break;
                case CutSortBomLine cutSort:
                    RunFabric(cutSort, resolved, snapshot, rounding, lines, errors);
                    break;
                case LaborBomLine labor:
                    RunLabor(labor, snapshot, rounding, lines, errors);
                    break;
            }
        }

        RunSurcharges(resolved, snapshot, rounding, lines);

        return (lines, errors);
    }

    // Frame: FrameBody.Price * Quantity, every matching FixedSurcharge, and SprayPrice when Colored.
    private static void RunFrame(FrameBomLine frame, CatalogueSnapshot snapshot, RoundingPolicy rounding, List<BreakdownLine> lines, List<PricingError> errors)
    {
        var frameBody = snapshot.FrameBodies.FirstOrDefault(f => f.Code == frame.FrameBodyCode);
        if (frameBody is null)
        {
            errors.Add(new PricingError(PricingErrorKind.UnknownFrameBody, frame.FrameBodyCode));
            return;
        }

        lines.Add(new BreakdownLine(
            BreakdownStage.Materials, "frame", $"Frame {frameBody.Code}", frame.LineKey,
            frame.Quantity, "pc", frameBody.Price, rounding.RoundLine(frameBody.Price * frame.Quantity)));

        // FixedSurcharges are global (not keyed to a specific frame body), so they are emitted once per
        // FrameBomLine encountered - an element with multiple Frame BOM lines would see them repeated once
        // per line. This matches typical fixtures (one Frame line per element); revisit if that changes.
        foreach (var surcharge in snapshot.FixedSurcharges.Where(s => s.AppliesToSection == BomSectionKind.Frame))
        {
            lines.Add(new BreakdownLine(
                BreakdownStage.Materials, "surcharge", $"Surcharge {surcharge.Name}", null,
                1m, "pc", surcharge.Amount, rounding.RoundLine(surcharge.Amount)));
        }

        if (frame.Colored)
        {
            var spray = snapshot.SprayPrices.FirstOrDefault(s => s.FrameBodyCode == frame.FrameBodyCode);
            if (spray is not null)
            {
                lines.Add(new BreakdownLine(
                    BreakdownStage.Materials, "spray", $"Spray {frameBody.Code}", null,
                    1m, "pc", spray.Price, rounding.RoundLine(spray.Price)));
            }
        }
    }

    // Foam: Material.UnitCost * Quantity (no conversion factor for foam).
    private static void RunFoam(FoamBomLine foam, CatalogueSnapshot snapshot, RoundingPolicy rounding, List<BreakdownLine> lines, List<PricingError> errors)
    {
        var material = snapshot.Materials.FirstOrDefault(m => m.Code == foam.FoamCode);
        if (material is null)
        {
            errors.Add(new PricingError(PricingErrorKind.UnknownMaterial, foam.FoamCode));
            return;
        }

        lines.Add(new BreakdownLine(
            BreakdownStage.Materials, "foam", $"Foam {material.Code}", foam.LineKey,
            foam.Quantity, "pc", material.UnitCost, rounding.RoundLine(material.UnitCost * foam.Quantity)));
    }

    // Cotton: Material.UnitCost * Measurement / UnitConversionFactor.
    // Measurement (not BomLine.Quantity) is the cost driver - CutUnits/Quantity feed labor separately
    // via explicit LaborBomLines, so Quantity is intentionally not multiplied in here.
    private static void RunCotton(CottonBomLine cotton, CatalogueSnapshot snapshot, RoundingPolicy rounding, List<BreakdownLine> lines, List<PricingError> errors)
    {
        var material = snapshot.Materials.FirstOrDefault(m => m.Code == cotton.CottonQualityCode);
        if (material is null)
        {
            errors.Add(new PricingError(PricingErrorKind.UnknownMaterial, cotton.CottonQualityCode));
            return;
        }

        var unitCost = material.UnitCost / cotton.UnitConversionFactor;
        lines.Add(new BreakdownLine(
            BreakdownStage.Materials, "cotton", $"Cotton {material.Code}", cotton.LineKey,
            cotton.Measurement, "m", unitCost, rounding.RoundLine(unitCost * cotton.Measurement)));
    }

    // Misc: Material.UnitCost * Quantity / UnitConversionFactor.
    private static void RunMisc(MiscBomLine misc, CatalogueSnapshot snapshot, RoundingPolicy rounding, List<BreakdownLine> lines, List<PricingError> errors)
    {
        var material = snapshot.Materials.FirstOrDefault(m => m.Code == misc.MaterialCode);
        if (material is null)
        {
            errors.Add(new PricingError(PricingErrorKind.UnknownMaterial, misc.MaterialCode));
            return;
        }

        var unitCost = material.UnitCost / misc.UnitConversionFactor;
        lines.Add(new BreakdownLine(
            BreakdownStage.Materials, "misc", $"Misc {material.Code}", misc.LineKey,
            misc.Quantity, "pc", unitCost, rounding.RoundLine(unitCost * misc.Quantity)));
    }

    // Fabric: primary Metrage against the element's resolved price group, plus each secondary
    // group's metrage resolved independently against the snapshot's PriceGroups.
    private static void RunFabric(CutSortBomLine cutSort, ResolvedElement resolved, CatalogueSnapshot snapshot, RoundingPolicy rounding, List<BreakdownLine> lines, List<PricingError> errors)
    {
        var rate = resolved.ResolvedPriceGroup.RatePerMeter;
        lines.Add(new BreakdownLine(
            BreakdownStage.Fabric, "fabric", $"Fabric {resolved.ResolvedPriceGroup.Code}", cutSort.LineKey,
            cutSort.Metrage, "m", rate, rounding.RoundLine(cutSort.Metrage * rate)));

        foreach (var (groupCode, metrage) in cutSort.SecondaryGroupMetrages)
        {
            var group = snapshot.PriceGroups.FirstOrDefault(g => g.Code == groupCode);
            if (group is null)
            {
                errors.Add(new PricingError(PricingErrorKind.NoPriceGroupForMaterialKind, groupCode));
                continue;
            }

            lines.Add(new BreakdownLine(
                BreakdownStage.Fabric, "fabric-secondary", $"Fabric (secondary) {group.Code}", cutSort.LineKey,
                metrage, "m", group.RatePerMeter, rounding.RoundLine(metrage * group.RatePerMeter)));
        }
    }

    // Labor: Units * Operation.UnitCost.
    private static void RunLabor(LaborBomLine labor, CatalogueSnapshot snapshot, RoundingPolicy rounding, List<BreakdownLine> lines, List<PricingError> errors)
    {
        var operation = snapshot.Operations.FirstOrDefault(o => o.Code == labor.OperationCode);
        if (operation is null)
        {
            errors.Add(new PricingError(PricingErrorKind.UnknownOperation, labor.OperationCode));
            return;
        }

        lines.Add(new BreakdownLine(
            BreakdownStage.Labor, "labor", $"Labor {operation.Code}", labor.LineKey,
            labor.Units, "unit", operation.UnitCost, rounding.RoundLine(labor.Units * operation.UnitCost)));
    }

    // Surcharges: ChoiceSurcharge for selected choice codes (scoped to element when ElementCode is set),
    // and CombinationPriceRule when every required selection matches.
    private static void RunSurcharges(ResolvedElement resolved, CatalogueSnapshot snapshot, RoundingPolicy rounding, List<BreakdownLine> lines)
    {
        var chosenChoiceCodes = resolved.Selection.ChoiceSelections.Values;

        foreach (var choiceSurcharge in snapshot.ChoiceSurcharges)
        {
            var isSelected = chosenChoiceCodes.Contains(choiceSurcharge.OptionChoiceCode);
            var appliesToElement = choiceSurcharge.ElementCode is null || choiceSurcharge.ElementCode == resolved.Element.Code;
            if (isSelected && appliesToElement)
            {
                lines.Add(new BreakdownLine(
                    BreakdownStage.Surcharges, "choice-surcharge", $"Choice surcharge {choiceSurcharge.OptionChoiceCode}", null,
                    1m, "pc", choiceSurcharge.Amount, rounding.RoundLine(choiceSurcharge.Amount)));
            }
        }

        foreach (var rule in snapshot.CombinationPriceRules)
        {
            var allMatch = rule.RequiredSelections.All(rs =>
                resolved.Selection.ChoiceSelections.TryGetValue(rs.OptionDefinitionCode, out var chosen) && chosen == rs.ChoiceCode);

            if (allMatch)
            {
                var description = string.Join(", ", rule.RequiredSelections.Select(rs => $"{rs.OptionDefinitionCode}:{rs.ChoiceCode}"));
                lines.Add(new BreakdownLine(
                    BreakdownStage.Surcharges, "combination", $"Combination ({description})", null,
                    1m, "pc", rule.Adjustment, rounding.RoundLine(rule.Adjustment)));
            }
        }
    }
}
