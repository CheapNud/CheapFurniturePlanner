namespace CheapFurniturePlanner.Models;

// One producer per model: a ModelCode maps to at most one Supplier (unique index on ModelCode
// enforces this), so a configured-element production unit's supplier is looked up by its order
// line's ModelCode rather than picked per line. SupplierId is nullable: a null-supplier row is an
// explicit in-house marker (PartyService.MarkModelInHouseAsync) rather than an unmapped model -
// it still occupies the unique ModelCode slot, so a model can never be both mapped and in-house.
// No nav to Supplier - callers that need the row go through SupplierId like every other soft FK
// in this file.
public class SupplierModelMap
{
    public int Id { get; set; }
    public int? SupplierId { get; set; }
    public required string ModelCode { get; set; }
}
