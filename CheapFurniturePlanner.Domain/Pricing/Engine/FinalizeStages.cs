namespace CheapFurniturePlanner.Domain.Pricing.Engine;

// Folds an element's cost-stage lines (Materials/Fabric/Labor/Surcharges, already computed by CostStages)
// into overhead + transport + a market-driven markup chain, producing the final per-element breakdown.
internal static class FinalizeStages
{
    // Every cost-stage-or-later stage that can carry a StageSubtotals entry, in the order lines are emitted.
    private static readonly BreakdownStage[] SubtotalStages =
        [BreakdownStage.Materials, BreakdownStage.Fabric, BreakdownStage.Labor, BreakdownStage.Surcharges, BreakdownStage.Overhead, BreakdownStage.Transport];

    internal static ElementBreakdown Run(ResolvedElement resolved, IReadOnlyList<BreakdownLine> costLines, PricingContext context)
    {
        var market = context.Market;
        var rounding = market.Rounding;

        var costBase = costLines.Sum(l => l.LineTotal);

        var overheadAmount = rounding.RoundLine(costBase * market.FixedCostPercent / 100m);
        var overheadLine = new BreakdownLine(BreakdownStage.Overhead, "overhead", "Overhead", null, 1m, "pc", overheadAmount, overheadAmount);

        var transportUnits = (decimal)resolved.Element.TransportUnits;
        var transportAmount = rounding.RoundLine(transportUnits * market.TransportRatePerUnit);
        var transportLine = new BreakdownLine(BreakdownStage.Transport, "transport", "Transport", null, transportUnits, "unit", market.TransportRatePerUnit, transportAmount);

        var chainBase = rounding.RoundSubtotal(costBase + overheadAmount + transportAmount);

        List<MarkupTraceEntry> markupTrace = [];
        var running = chainBase;
        foreach (var step in market.MarkupSteps)
        {
            running = step.Mode switch
            {
                MarkupMode.Compound => running + running * step.Percent / 100m,
                MarkupMode.Additive => running + chainBase * step.Percent / 100m,
                _ => throw new ArgumentOutOfRangeException(nameof(step), step.Mode, "Unknown markup mode.")
            };
            markupTrace.Add(new MarkupTraceEntry(step.Name, step.Percent, step.Mode.ToString(), running));
        }

        // Seller multiplier is context, not catalogue - it does not get its own MarkupTrace entry.
        running *= context.SellerMultiplier;

        var elementUnitPrice = rounding.RoundFinal(running);
        var elementTotal = elementUnitPrice * resolved.Selection.Quantity;

        List<BreakdownLine> lines = [.. costLines, overheadLine, transportLine];

        Dictionary<string, decimal> stageSubtotals = [];
        foreach (var stage in SubtotalStages)
        {
            var stageLines = lines.Where(l => l.Stage == stage).ToList();
            if (stageLines.Count > 0)
            {
                stageSubtotals[stage.ToString()] = stageLines.Sum(l => l.LineTotal);
            }
        }
        stageSubtotals["ChainBase"] = chainBase;
        stageSubtotals["UnitPrice"] = elementUnitPrice;

        return new ElementBreakdown(resolved.Element.Code, resolved.VariantCodeValue, resolved.Selection.Quantity, lines, stageSubtotals, markupTrace, elementTotal);
    }
}
