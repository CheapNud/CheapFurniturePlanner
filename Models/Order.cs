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
    public string? PinnedCatalogueVersion { get; set; }
    public string? PinnedContentHash { get; set; }
    public OrderState State { get; set; } = OrderState.Draft;
    public DateTime CreatedAt { get; set; }
    public DateTime? PlacedAt { get; set; }
    public decimal OrderDiscountPercent { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}
