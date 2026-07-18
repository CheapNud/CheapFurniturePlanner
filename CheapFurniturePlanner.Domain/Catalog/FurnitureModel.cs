namespace CheapFurniturePlanner.Domain.Catalog;

public class FurnitureModel : TradeItem
{
    public required string Name { get; set; }
    public string? CollectionCode { get; set; }
    public ModelType? ModelType { get; set; }
    public List<Element> Elements { get; set; } = [];
}
