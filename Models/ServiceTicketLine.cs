namespace CheapFurniturePlanner.Models;

// A complaint item: either points at an order line or is free text (standalone tickets).
public class ServiceTicketLine
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public int? OrderLineId { get; set; }
    public required string Description { get; set; }
}
