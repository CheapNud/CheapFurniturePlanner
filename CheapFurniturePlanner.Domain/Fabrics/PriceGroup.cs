namespace CheapFurniturePlanner.Domain.Fabrics;

public class PriceGroup
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public MaterialKind Kind { get; set; }
    public decimal RatePerMeter { get; set; }
    // Material type/quality that drives BOM operations (fabric vs leather vs thick-leather).
    // Null falls back to Kind. This is the BOM-significant material axis; color is not.
    public string? MaterialTypeCode { get; set; }
}
