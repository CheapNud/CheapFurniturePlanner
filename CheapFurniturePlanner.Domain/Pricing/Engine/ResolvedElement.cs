using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;

namespace CheapFurniturePlanner.Domain.Pricing.Engine;

// A BOM line paired with the section it lives in - CostStages needs the section kind to match
// FixedSurcharges (which are keyed by BomSectionKind, not by line type).
internal record EffectiveLine(BomSectionKind Section, BomLine Line);

internal record ResolvedElement(Element Element, ElementSelection Selection, string VariantCodeValue, PriceGroup ResolvedPriceGroup, IReadOnlyList<EffectiveLine> EffectiveLines);
