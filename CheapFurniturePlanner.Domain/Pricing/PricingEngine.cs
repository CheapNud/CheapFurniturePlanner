using CheapFurniturePlanner.Domain.Pricing.Engine;

namespace CheapFurniturePlanner.Domain.Pricing;

public static class PricingEngine
{
    // Resolve -> per-element CostStages -> FinalizeStages -> assemble PriceBreakdown.
    // Any error anywhere (resolve or cost stage) short-circuits to an errors-only result with a null Breakdown.
    public static PricingResult Calculate(PricingRequest request)
    {
        var (resolvedElements, resolveErrors) = ResolveStage.Run(request);
        if (resolveErrors.Count > 0)
        {
            return new PricingResult { Errors = resolveErrors };
        }

        var rounding = request.Context.Market.Rounding;
        List<PricingError> costErrors = [];
        List<(ResolvedElement Resolved, List<BreakdownLine> Lines)> costed = [];

        foreach (var resolved in resolvedElements)
        {
            var (lines, errors) = CostStages.Run(resolved, request.Snapshot, rounding);
            costErrors.AddRange(errors);
            costed.Add((resolved, lines));
        }

        if (costErrors.Count > 0)
        {
            return new PricingResult { Errors = costErrors };
        }

        var elements = costed.Select(c => FinalizeStages.Run(c.Resolved, c.Lines, request.Context)).ToList();

        var breakdown = new PriceBreakdown(
            request.Snapshot.Version,
            request.Snapshot.ContentHash,
            request.Context.Market.Code,
            elements,
            elements.Sum(e => e.ElementTotal));

        return new PricingResult { Breakdown = breakdown };
    }
}
