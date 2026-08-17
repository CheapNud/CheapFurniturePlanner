using CheapFurniturePlanner.Domain.Production;

namespace CheapFurniturePlanner.Models;

// Supply-side terms for one material identity from one supplier - lead time, MOQ, package size,
// price. A material identity can carry terms from several suppliers; exactly one is IsPreferred
// at a time (service-enforced atomic swap, not a state machine - see the terms editor task).
// Unique on (Kind, Code, HardnessCode, SupplierId).
public class MaterialSupplierTerm
{
    public int Id { get; set; }
    public MaterialKind Kind { get; set; }
    public required string Code { get; set; }
    public string? HardnessCode { get; set; }
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public int DeliveryTimeDays { get; set; }
    public decimal? MinimumOrderQuantity { get; set; }
    public decimal? UnitsPerPackage { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool IsPreferred { get; set; }
}
