using CheapFurniturePlanner.Domain.Production;

namespace CheapFurniturePlanner.Models;

// One inventory row per (Kind, Code, HardnessCode) - a running balance, not a ledger; whatever
// consumes or replenishes stock adjusts Amount in place. HardnessCode narrows a Foam row to one
// density (mirrors MaterialNeedLine.HardnessCode); every other kind leaves it null.
public class MaterialStock
{
    public int Id { get; set; }
    public MaterialKind Kind { get; set; }
    public required string Code { get; set; }
    public string? HardnessCode { get; set; }
    public decimal Amount { get; set; }
    public DateTime UpdatedAt { get; set; }
}
