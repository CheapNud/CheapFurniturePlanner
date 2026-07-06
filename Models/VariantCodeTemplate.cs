namespace CheapFurniturePlanner.Models;

public class VariantCodeTemplate
{
    public int Id { get; set; }
    public required string ModelCode { get; set; }
    public required string VariantCode { get; set; }
    public string? SuggestedCode { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
