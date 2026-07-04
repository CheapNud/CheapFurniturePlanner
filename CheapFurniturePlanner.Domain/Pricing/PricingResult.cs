namespace CheapFurniturePlanner.Domain.Pricing;

public class PricingResult
{
    public List<PricingError> Errors { get; init; } = [];
    public PriceBreakdown? Breakdown { get; init; }
    public bool IsSuccess => Errors.Count == 0;
}
