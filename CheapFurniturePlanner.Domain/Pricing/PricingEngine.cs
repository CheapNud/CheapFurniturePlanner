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

        // The snapshot is authoritative for market parameters: ResolveStage has already confirmed
        // the context market's code exists in the snapshot, so re-resolve the market from there and
        // price with that instance instead. Context.Market is treated as a request (which market to
        // use), not as the source of truth for its rates/rounding/markup.
        var market = request.Snapshot.Markets.Single(m => m.Code == request.Context.Market.Code);
        var context = request.Context with { Market = market };

        var rounding = market.Rounding;
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

        var elements = costed.Select(c => FinalizeStages.Run(c.Resolved, c.Lines, context)).ToList();

        var breakdown = new PriceBreakdown(
            request.Snapshot.Version,
            request.Snapshot.ContentHash,
            market.Code,
            elements,
            elements.Sum(e => e.ElementTotal));

        return new PricingResult { Breakdown = breakdown };
    }
}
