namespace CheapFurniturePlanner.Domain.Fabrics;

public class PriceGroup
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public MaterialKind Kind { get; set; }
    public decimal RatePerMeter { get; set; }
}
