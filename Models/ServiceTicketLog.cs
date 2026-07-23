namespace CheapFurniturePlanner.Models;

public class ServiceTicketLog
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public DateTime At { get; set; }
    public required string UserId { get; set; }
    public required string Message { get; set; }
}
