namespace CheapFurniturePlanner.Models;

public enum SupplierOrderState { Draft, Sent, Completed }

// A purchase order sent to one supplier. There is no separate PO-line entity - the ProductionUnits
// linked via SupplierOrderId (SetNull on delete) ARE the PO's lines, so a unit already carries its
// own model/element identity and the PO just groups a batch of them under one PoNumber.
public class SupplierOrder
{
    public int Id { get; set; }
    public required string PoNumber { get; set; }
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public SupplierOrderState State { get; set; } = SupplierOrderState.Draft;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? TheirReference { get; set; }
    public required string CreatedByUserId { get; set; }
    public List<ProductionUnit> Units { get; set; } = [];
}
