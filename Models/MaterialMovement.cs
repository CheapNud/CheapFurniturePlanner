using CheapFurniturePlanner.Domain.Production;

namespace CheapFurniturePlanner.Models;

public enum MaterialMovementType { Receipt, Backflush, BackflushUndo, Adjustment }

// One audit row per stock mutation - unlike MaterialStock (a running balance), this is an
// append-only log feeding the computed average usage (MaterialNeedsService). Quantity is signed
// the same way the mutation it records signed the stock change: Receipt/BackflushUndo positive,
// Backflush negative, Adjustment the delta between old and new absolute amount (can be either
// sign). Written in the SAME SaveChanges as the stock mutation at all four sites - a failed
// mutation must never leave an orphaned movement row, or vice versa.
public class MaterialMovement
{
    public int Id { get; set; }
    public MaterialKind Kind { get; set; }
    public required string Code { get; set; }
    public string? HardnessCode { get; set; }
    public decimal Quantity { get; set; }
    public MaterialMovementType Type { get; set; }
    public DateTime OccurredAt { get; set; }
    // MPO number for a receipt, unit UnitCode for a backflush/undo, null for a manual adjustment.
    public string? Reference { get; set; }
    public string? UserId { get; set; }
}
