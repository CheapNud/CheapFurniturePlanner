using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Configurator;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.ViewModels;

namespace CheapFurniturePlanner.Services;

public sealed class PricingService(ICatalogueSource catalogue)
{
    public async Task<PricingResult> PriceAsync(FurniturePlannerViewModel placement)
    {
        if (string.IsNullOrEmpty(placement.ElementCode))
        {
            return new PricingResult { Errors = [new PricingError(PricingErrorKind.UnknownElement, placement.ElementCode ?? "")] };
        }
        var snapshot = await catalogue.GetCurrentAsync();
        var modelCode = ConfigurationResolver.FindModelCode(snapshot, placement.ElementCode);
        if (modelCode is null)
        {
            return new PricingResult { Errors = [new PricingError(PricingErrorKind.UnknownElement, placement.ElementCode)] };
        }
        if (snapshot.Markets.Count == 0)
        {
            return new PricingResult { Errors = [new PricingError(PricingErrorKind.UnknownMarket, "")] };
        }
        var config = new ProductConfiguration(modelCode,
            [new ElementSelection(placement.ElementCode, 1, placement.Selections, placement.FabricColorCode)]);
        var market = snapshot.Markets[0]; // demo uses the first market; market selection is a later phase
        var request = new PricingRequest(snapshot, config, new PricingContext(market));
        return PricingEngine.Calculate(request);
    }
}
