namespace CheapFurniturePlanner.Models;

public class VariantNaming
{
    public int Id { get; set; }
    public required string ModelCode { get; set; }
    public required string VariantCode { get; set; }
    public required string AssignedCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
