namespace CheapFurniturePlanner.Models;

public enum MaterialOrderState { Draft, Sent, Completed }

// A purchase order sent to one supplier for raw materials (foam, frame stock, cotton, fabric,
// misc) - SupplierOrder's counterpart for in-house material needs. Unlike SupplierOrder, a
// material need has no ProductionUnit to double as its line, so MaterialOrderLine is a real,
// stored line table rather than a derived view.
public class MaterialOrder
{
    public int Id { get; set; }
    public required string Number { get; set; }
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public MaterialOrderState State { get; set; } = MaterialOrderState.Draft;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? TheirReference { get; set; }
    public required string CreatedByUserId { get; set; }
    public List<MaterialOrderLine> Lines { get; set; } = [];
}
