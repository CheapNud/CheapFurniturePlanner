namespace CheapFurniturePlanner.Models;

public class AuthoringModelDocument
{
    public int Id { get; set; }
    public required string ModelCode { get; set; }
    public int SortOrder { get; set; }
    public required string BundleJson { get; set; }
    // MB1: optimistic concurrency token. SQLite has no rowversion, so this is a plain int bumped
    // explicitly by AuthoringCatalogueStore's save path (see IsConcurrencyToken in
    // FurniturePlannerContext) rather than a DB-generated value.
    public int Version { get; set; }
}
