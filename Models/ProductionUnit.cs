namespace CheapFurniturePlanner.Models;

public enum ProductionUnitState { Expected, Arrived, Delivered, Cancelled }

// One row per physical piece the dock expects: spawned at order placement (one per quantity of
// each deliver-to-warehouse line), scanned Arrived at receiving, loaded onto a Trip, Delivered
// when the trip departs. The order's production phase is derived from these rows - units are the
// single source of truth, orders never store a production status.
public class ProductionUnit
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int OrderLineId { get; set; }
    public int SequenceNumber { get; set; }
    public required string UnitCode { get; set; }
    public ProductionUnitState State { get; set; } = ProductionUnitState.Expected;
    public DateTime? ArrivedAt { get; set; }
    public int? TripId { get; set; }
    public Trip? Trip { get; set; }
    public int? LoadPosition { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
    // Purchasing links - both optional and independent of Trip: a unit is ordered from a supplier,
    // separately announced as inbound, and separately assigned to a truck. None of the three imply
    // the others.
    public int? SupplierOrderId { get; set; }
    public SupplierOrder? SupplierOrder { get; set; }
    public int? SupplierDeliveryId { get; set; }
    public SupplierDelivery? SupplierDelivery { get; set; }
}
