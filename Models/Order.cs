namespace CheapFurniturePlanner.Models;

public enum OrderState { Draft, Placed, Cancelled }

// An order pins the published catalogue version its first line was added under
// (PinnedCatalogueVersion/PinnedContentHash, null until then); every later line resolves and prices
// against that same version, so later price edits never move it. Prices live on the lines as
// add-time snapshots; OrderPrice is recomputed by the service on every mutation.
public class Order
{
    public int Id { get; set; }
    public required string OrderNumber { get; set; }
    public int SellerId { get; set; }
    public Seller? Seller { get; set; }
    public int ConsumerId { get; set; }
    public Consumer? Consumer { get; set; }
    public required string MarketCode { get; set; }
    public int? DeliveryAddressId { get; set; }
    public Address? DeliveryAddress { get; set; }
    public string? PinnedCatalogueVersion { get; set; }
    public string? PinnedContentHash { get; set; }
    public OrderState State { get; set; } = OrderState.Draft;
    public DateTime CreatedAt { get; set; }
    public DateTime? PlacedAt { get; set; }
    // Promised to the consumer, set by the office - NEVER derived from a trip's departure date
    // (the legacy system silently equated the two; planning surfaces a warning instead).
    public DateTime? PromisedDeliveryDate { get; set; }
    public decimal OrderDiscountPercent { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}
