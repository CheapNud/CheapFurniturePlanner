namespace CheapFurniturePlanner.Models;

// Registry row attaching a catalogue collection code to a firm. Soft link by exact string
// equality with FurnitureModel.CollectionCode (the Article.SupplierRef philosophy): catalogue
// codes are not validated against this registry - unknown codes resolve to the default firm.
public class Collection
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int FirmId { get; set; }
    public Firm? Firm { get; set; }
}
