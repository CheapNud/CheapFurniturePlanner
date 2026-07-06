namespace CheapFurniturePlanner.Domain.Pricing;

public record PricingRequest(CatalogueSnapshot Snapshot, ProductConfiguration Configuration, PricingContext Context);
