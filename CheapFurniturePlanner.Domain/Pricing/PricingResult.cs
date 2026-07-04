namespace CheapFurniturePlanner.Domain.Pricing;

public class PricingResult
{
    // NOTE: Task 7 ships this class WITHOUT a Breakdown property (errors-only).
    // Task 9 adds: public PriceBreakdown? Breakdown { get; init; }
    public List<PricingError> Errors { get; init; } = [];
    public bool IsSuccess => Errors.Count == 0;
}
