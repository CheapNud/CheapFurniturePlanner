namespace CheapFurniturePlanner.Models;

// One producer per model: a ModelCode maps to exactly one Supplier (unique index on ModelCode
// enforces this), so a configured-element production unit's supplier is looked up by its order
// line's ModelCode rather than picked per line. No nav to Supplier - callers that need the row
// go through SupplierId like every other soft FK in this file.
public class SupplierModelMap
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public required string ModelCode { get; set; }
}
