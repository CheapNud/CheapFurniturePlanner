using CheapFurniturePlanner.Domain.Production;

namespace CheapFurniturePlanner.Models;

// One purchasable material line on a MaterialOrder. QuantityReceived tracks partial receipt
// against QuantityOrdered - this task only wires the schema, the receipt flow is a later task.
public class MaterialOrderLine
{
    public int Id { get; set; }
    public int MaterialOrderId { get; set; }
    public MaterialKind Kind { get; set; }
    public required string Code { get; set; }
    public string? HardnessCode { get; set; }
    public string? DisplayName { get; set; }
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityReceived { get; set; }
    // Snapshotted from the preferred supplier term at line creation - never re-read, so a later
    // term price change never moves an existing line (order-entry task wires the snapshot; a
    // manually added line with no preferred term just leaves this null, unpriced).
    public decimal? UnitPrice { get; set; }
}
