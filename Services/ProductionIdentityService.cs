using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.ViewModels;

namespace CheapFurniturePlanner.Services;

public sealed class ProductionIdentityService(ICatalogueSource catalogue)
{
    public async Task<ProductionIdentity?> ResolveForPlacementAsync(FurniturePlannerViewModel placement, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(placement.ElementCode)) { return null; }
        var snapshot = await catalogue.GetCurrentAsync();
        var model = snapshot.Models.FirstOrDefault(m => m.Elements.Any(e => e.Code == placement.ElementCode));
        if (model is null) { return null; }
        var config = new ProductConfiguration(model.Code,
            [new ElementSelection(placement.ElementCode, 1, placement.Selections, placement.FabricColorCode)]);
        // Placed models are always published (Active); no suggestions exist in this slice → status Composed.
        return ProductionIdentityResolver.Resolve(snapshot, config, new Dictionary<string, string>(), TradeItemState.Active).FirstOrDefault();
    }
}
