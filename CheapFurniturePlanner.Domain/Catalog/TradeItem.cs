namespace CheapFurniturePlanner.Domain.Catalog;

public abstract class TradeItem
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public string? Gtin { get; set; }
    public int DisplayIndex { get; set; }
    public TradeItemState State { get; set; } = TradeItemState.Active;
}
