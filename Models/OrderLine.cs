namespace CheapFurniturePlanner.Models;

public enum OrderLineKind { ConfiguredElement, StandaloneArticle }

// A line snapshots what was ordered and at what price. Configured-element lines carry the full
// configuration (SelectionsJson + FabricColorCode) plus the bridge result: ArticleId/AssignedCode
// when the variant was named, otherwise AssignedCode null and VariantCode (the composed code) is
// the production identity. Standalone lines carry the article's flat ManualPrice and SupplierRef.
public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int DisplayIndex { get; set; }
    public OrderLineKind Kind { get; set; }
    public int? ArticleId { get; set; }
    public string? AssignedCode { get; set; }
    public string? ModelCode { get; set; }
    public string? ElementCode { get; set; }
    public string? VariantCode { get; set; }
    public string SelectionsJson { get; set; } = "{}";
    public string? FabricColorCode { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? DiscountSource { get; set; }
    public bool DiscountIsManual { get; set; }
    public string? SupplierRef { get; set; }
}
