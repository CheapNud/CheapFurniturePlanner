namespace CheapFurniturePlanner.Domain.Pricing;

public record PricingContext(MarketParameters Market, decimal SellerMultiplier = 1m);
