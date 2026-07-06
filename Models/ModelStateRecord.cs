using CheapFurniturePlanner.Domain.Catalog;
namespace CheapFurniturePlanner.Models;

public class ModelStateRecord
{
    public int Id { get; set; }
    public required string ModelCode { get; set; }
    public TradeItemState State { get; set; } = TradeItemState.Draft;
}
