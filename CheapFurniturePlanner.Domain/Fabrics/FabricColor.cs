using CheapFurniturePlanner.Domain.Catalog;

namespace CheapFurniturePlanner.Domain.Fabrics;

public class FabricColor : TradeItem
{
    public required string Name { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal ShippingCost { get; set; }
    public FabricUnit Unit { get; set; } = FabricUnit.Meter;
}
