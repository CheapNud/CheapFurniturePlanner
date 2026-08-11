using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Pricing.Engine;

namespace CheapFurniturePlanner.Domain.Production;

public enum MaterialKind { Foam, Frame, Cotton, Fabric, Misc }

// One purchasable material requirement of a single unit's configuration.
public sealed record MaterialNeedLine(MaterialKind Kind, string Code, string? HardnessCode, string? FabricColorCode, decimal Quantity);

// The ONE seam for "what materials does this configured unit consume": rides the pricing
// engine's own ResolveStage (visibility, applicability conditions, substitution rules), so
// forecast and backflush can never disagree with costing. Labor and surcharges are not
// materials and never appear here.
public static class MaterialRequirements
{
    public static IReadOnlyList<MaterialNeedLine> Resolve(CatalogueSnapshot snapshot, string modelCode,
        string elementCode, IReadOnlyDictionary<string, string> selections, string? fabricColorCode)
    {
        // ResolveStage validates the market; BOM applicability is market-independent, so any
        // snapshot market satisfies the request shape.
        var market = snapshot.Markets.FirstOrDefault()
            ?? throw new InvalidOperationException("Snapshot has no markets.");
        var configuration = new ProductConfiguration(modelCode,
            [new ElementSelection(elementCode, 1, selections, fabricColorCode)]);
        var (resolved, resolveErrors) = ResolveStage.Run(new PricingRequest(snapshot, configuration,
            new PricingContext(market, 1m)));
        var element = resolved.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Element '{elementCode}' in model '{modelCode}' did not resolve: " +
                string.Join("; ", resolveErrors.Select(e => $"{e.Kind}: {e.Subject}")));

        List<MaterialNeedLine> needLines = [];
        foreach (var effective in element.EffectiveLines)
        {
            switch (effective.Line)
            {
                case FoamBomLine foam:
                    needLines.Add(new(MaterialKind.Foam, foam.FoamCode, foam.HardnessCode, null, foam.Quantity));
                    break;
                case FrameBomLine frame:
                    needLines.Add(new(MaterialKind.Frame, frame.FrameBodyCode, null, null, frame.Quantity));
                    break;
                case CottonBomLine cotton:
                    needLines.Add(new(MaterialKind.Cotton, cotton.CottonQualityCode, null, null, cotton.Measurement));
                    break;
                case CutSortBomLine cutSort when fabricColorCode is not null:
                    needLines.Add(new(MaterialKind.Fabric, fabricColorCode, null, fabricColorCode, cutSort.Metrage));
                    break;
                case MiscBomLine misc:
                    needLines.Add(new(MaterialKind.Misc, misc.MaterialCode, null, null, misc.Quantity));
                    break;
            }
        }
        return needLines;
    }
}
