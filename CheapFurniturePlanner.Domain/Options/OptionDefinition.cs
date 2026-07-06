using CheapFurniturePlanner.Domain.Catalog;

namespace CheapFurniturePlanner.Domain.Options;

public class OptionDefinition : TradeItem
{
    public required string Name { get; set; }
    public OptionKind Kind { get; set; }
    public bool ExcludeFromDiscount { get; set; }
}
