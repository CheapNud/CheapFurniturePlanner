namespace CheapFurniturePlanner.Models;

public enum TripState { Planning, Departed }

// An outbound truck run: arrived units are assigned with a load position while Planning;
// Departed delivers every assigned unit and is terminal. Truck/driver are free text - no
// fleet master data.
public class Trip
{
    public int Id { get; set; }
    public required string TripCode { get; set; }
    public DateTime? DepartureDate { get; set; }
    public string? TruckName { get; set; }
    public string? DriverName { get; set; }
    public TripState State { get; set; } = TripState.Planning;
    public DateTime? DepartedAt { get; set; }
    public List<ProductionUnit> Units { get; set; } = [];
}
