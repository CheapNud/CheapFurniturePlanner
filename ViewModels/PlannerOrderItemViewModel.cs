namespace CheapFurniturePlanner.ViewModels;

/// <summary>
/// ViewModel for planner order items (equivalent to original order functionality)
/// </summary>
public class PlannerOrderItemViewModel
{
    public int Id { get; set; }
    public FurniturePlannerViewModel Furniture { get; set; } = new();
    public int Quantity { get; set; } = 1;
    public decimal? UnitPrice { get; set; }
    public decimal? TotalPrice => UnitPrice * Quantity;
    public string? Notes { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}