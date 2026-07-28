namespace CheapFurniturePlanner.Models;

public enum TripState { Planning, Departed, Completed }

// An outbound truck run: arrived units are assigned with a load position while Planning;
// Departed no longer delivers anything by itself - units are confirmed delivered one at a
// time, and the trip completes itself when the last assigned unit is confirmed. Truck/driver
// are free text - no fleet master data.
public class Trip
{
    public int Id { get; set; }
    public required string TripCode { get; set; }
    public DateTime? DepartureDate { get; set; }
    public string? TruckName { get; set; }
    public string? DriverName { get; set; }
    public int? RegionId { get; set; }
    public Region? Region { get; set; }
    public TripState State { get; set; } = TripState.Planning;
    public DateTime? DepartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<ProductionUnit> Units { get; set; } = [];
}
