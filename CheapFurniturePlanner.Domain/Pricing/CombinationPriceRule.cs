using CheapFurniturePlanner.Domain.Bom;

namespace CheapFurniturePlanner.Domain.Pricing;

public record CombinationPriceRule(IReadOnlyList<SelectionKey> RequiredSelections, decimal Adjustment);
