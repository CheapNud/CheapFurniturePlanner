namespace CheapFurniturePlanner.Models;

// A supplier's announcement that a batch of units is inbound (Reference is their delivery note
// number, unique per supplier). Like SupplierOrder there is no separate line entity - the linked
// ProductionUnits (SetNull on delete) are the announcement's contents, and "received" is never
// stored here: it is derived from those units reaching ProductionUnitState.Arrived, same as every
// other production-phase read.
public class SupplierDelivery
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public required string Reference { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string CreatedByUserId { get; set; }
    public List<ProductionUnit> Units { get; set; } = [];
}
