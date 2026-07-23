namespace CheapFurniturePlanner.Models;

public enum ServiceTicketState { New, InProgress, Resolved, Cancelled }
public enum ServiceFlow { Undecided, Internal, External }

// The slim service core: one ticket, one state machine, one flow choice. The flow-specific
// data lives in the 1:1 InternalRepair / SupplierReport rows so neither flow's columns pollute
// the other (the legacy system kept both flows in one wide row and it never untangled).
public class ServiceTicket
{
    public int Id { get; set; }
    public required string TicketNumber { get; set; }
    public int ConsumerId { get; set; }
    public Consumer? Consumer { get; set; }
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public required string CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string ProblemDescription { get; set; }
    public string? VisitAddress { get; set; }
    public ServiceTicketState State { get; set; } = ServiceTicketState.New;
    public ServiceFlow Flow { get; set; } = ServiceFlow.Undecided;
    public DateTime? ResolvedAt { get; set; }
    public string? CancelReason { get; set; }
    public List<ServiceTicketLine> Lines { get; set; } = [];
    public List<ServiceTicketLog> Logs { get; set; } = [];
    public List<ServiceTicketPhoto> Photos { get; set; } = [];
    public InternalRepair? InternalRepair { get; set; }
    public SupplierReport? SupplierReport { get; set; }
}
