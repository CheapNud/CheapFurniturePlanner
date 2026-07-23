namespace CheapFurniturePlanner.Models;

public enum RepairOutcome { NotResolved, GoodsTaken, Unfounded, Resolved }

// Internal flow (1:1 with the ticket, TicketId is the PK): dispatch one of our own mechanics.
// Duration is computed (departure - arrival), never stored; mechanic mileage totals are a SUM
// over these rows, never denormalized onto the user.
public class InternalRepair
{
    public int TicketId { get; set; }
    public string? AssignedUserId { get; set; }
    public DateTime? ExecutionDate { get; set; }
    public TimeSpan? ArrivalTime { get; set; }
    public TimeSpan? DepartureTime { get; set; }
    public int? MileageBefore { get; set; }
    public int? MileageAfter { get; set; }
    public RepairOutcome? Outcome { get; set; }
    public string? SolutionDescription { get; set; }
    public string? InternalRemark { get; set; }
}
