namespace CheapFurniturePlanner.Models;

public enum PhotoKind { Before, After }

// FileName is the stored (GUID-based) name under the ServicePhotoStore root; bytes never
// enter the database.
public class ServiceTicketPhoto
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public PhotoKind Kind { get; set; }
    public required string FileName { get; set; }
    public DateTime UploadedAt { get; set; }
    public required string UserId { get; set; }
}
