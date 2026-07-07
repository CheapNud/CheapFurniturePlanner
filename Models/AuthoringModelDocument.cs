namespace CheapFurniturePlanner.Models;

public class AuthoringModelDocument
{
    public int Id { get; set; }
    public required string ModelCode { get; set; }
    public int SortOrder { get; set; }
    public required string BundleJson { get; set; }
}
