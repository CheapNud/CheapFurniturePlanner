namespace CheapFurniturePlanner.Domain.Fabrics;

public class FabricGroup
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string PriceGroupCode { get; set; }
    public List<FabricColor> Colors { get; set; } = [];
}
