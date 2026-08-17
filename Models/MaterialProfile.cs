using CheapFurniturePlanner.Domain.Production;

namespace CheapFurniturePlanner.Models;

// Demand-side MRP knobs for one material identity - separate from MaterialStock (the running
// balance) so a material can be authored (minimum stock, a manual usage override) before it ever
// has a stock row. Unique on (Kind, Code, HardnessCode), same identity key as MaterialStock.
public class MaterialProfile
{
    public int Id { get; set; }
    public MaterialKind Kind { get; set; }
    public required string Code { get; set; }
    public string? HardnessCode { get; set; }
    public decimal MinimumStock { get; set; }
    // Per week. Null means "let the computed trailing-56-day average decide" (MaterialNeedsService).
    public decimal? AverageUsageOverride { get; set; }
}
