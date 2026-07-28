namespace CheapFurniturePlanner.Models;

// A consumer's delivery-address book entry. Exactly one default per consumer, enforced in
// PartyService (first added auto-defaults, setting a new default clears the previous).
public class ConsumerDeliveryAddress
{
    public int Id { get; set; }
    public int ConsumerId { get; set; }
    public int AddressId { get; set; }
    public Address? Address { get; set; }
    public required string Label { get; set; }
    public bool IsDefault { get; set; }
}
