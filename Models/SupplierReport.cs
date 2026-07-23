namespace CheapFurniturePlanner.Models;

// External flow (1:1 with the ticket, TicketId is the PK): the case goes to the supplier.
// SupplierRef is prefilled from the first linked dropship order line; Decision arriving
// resolves the ticket. Tracking is manual - portal/chat/mail integration is deferred.
public class SupplierReport
{
    public int TicketId { get; set; }
    public string SupplierRef { get; set; } = "";
    public DateTime? ReportedAt { get; set; }
    public string? SupplierCaseNumber { get; set; }
    public string? Decision { get; set; }
    public string? DecisionNote { get; set; }
}
