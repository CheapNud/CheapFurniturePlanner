namespace CheapFurniturePlanner.Models;

// The fourth party becomes a real entity (previously only free-text SupplierRef strings).
// Code is the stable identity the catalogue's Article.SupplierRef soft-links to.
public class Supplier
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int? AddressId { get; set; }
    public Address? Address { get; set; }
}
