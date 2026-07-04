using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;

namespace CheapFurniturePlanner.Domain.Pricing.Engine;

internal record ResolvedElement(Element Element, ElementSelection Selection, string VariantCodeValue, PriceGroup ResolvedPriceGroup, IReadOnlyList<BomLine> EffectiveLines);
