using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.ViewModels;

namespace CheapFurniturePlanner.Services;

public sealed class ProductionIdentityService(ICatalogueSource catalogue, CodeAssignmentService assignments)
{
    // register=true also creates/ensures a VariantCodeTemplates row for the resolved variant (the
    // modellenkamer authoring workflow's registry); pass register=false for read-only display of a
    // config that hasn't (or shouldn't) be committed to that registry, e.g. a degraded/invalid config
    // that still resolves a variant code but has no business creating a permanent template row.
    public async Task<ProductionIdentity?> ResolveForPlacementAsync(FurniturePlannerViewModel placement, bool register = true, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(placement.ElementCode)) { return null; }
        var snapshot = await catalogue.GetCurrentAsync();
        var model = snapshot.Models.FirstOrDefault(m => m.Elements.Any(e => e.Code == placement.ElementCode));
        if (model is null) { return null; }

        var config = new ProductConfiguration(model.Code,
            [new ElementSelection(placement.ElementCode, 1, placement.Selections, placement.FabricColorCode)]);

        if (register)
        {
            // Resolve once to obtain the variant, register it, then resolve with current suggestions + state.
            var seed = ProductionIdentityResolver.Resolve(snapshot, config, new Dictionary<string, string>(), Domain.Catalog.TradeItemState.Draft);
            if (seed.Count == 0) { return null; }
            await assignments.RegisterVariantAsync(model.Code, seed[0].VariantCode, ct);
        }

        var suggestions = await assignments.SuggestionsForModelAsync(model.Code, ct);
        var state = await assignments.GetModelStateAsync(model.Code, ct);
        return ProductionIdentityResolver.Resolve(snapshot, config, suggestions, state).FirstOrDefault();
    }
}
