namespace CheapFurniturePlanner.Models;

public class AuthoringArticlesDocument
{
    public int Id { get; set; }
    public required string BundleJson { get; set; }
    // MB1: optimistic concurrency token - see AuthoringModelDocument.Version for why it's a plain int.
    public int Version { get; set; }
}
