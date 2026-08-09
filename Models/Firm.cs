namespace CheapFurniturePlanner.Models;

// Our own legal entity - a firm is one accounting ledger (own VAT number, own bank account).
// Exactly one firm is the default (service-enforced): the fallback issuer/buyer for documents
// whose order does not route to a specific firm through its collection.
public class Firm
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int? AddressId { get; set; }
    public Address? Address { get; set; }
    public string? VatNumber { get; set; }
    public string? Iban { get; set; }
    public string? Bic { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? PeppolEndpointId { get; set; }
    public bool IsDefault { get; set; }
}
