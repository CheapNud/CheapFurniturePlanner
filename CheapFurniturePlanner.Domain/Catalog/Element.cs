using CheapFurniturePlanner.Domain.Options;

namespace CheapFurniturePlanner.Domain.Catalog;

public class Element : TradeItem
{
    public required string Name { get; set; }
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Height { get; set; }
    public int TransportUnits { get; set; }
    public List<ProductOption> Options { get; set; } = [];
}
