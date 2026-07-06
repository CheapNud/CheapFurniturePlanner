namespace CheapFurniturePlanner.Domain.Pricing;

public record MarketParameters(string Code, decimal TransportRatePerUnit, decimal FixedCostPercent, IReadOnlyList<MarkupStep> MarkupSteps, RoundingPolicy Rounding);
