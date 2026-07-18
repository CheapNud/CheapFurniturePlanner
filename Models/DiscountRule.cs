using CheapFurniturePlanner.Domain.Catalog;

namespace CheapFurniturePlanner.Models;

public enum DiscountScope { ElementPriceGroup, Model, ModelType, MaterialType, Everything }

// One row of the seller's discount rule list — the legacy cascade tiers as data. CollectionCode
// null = any collection; a collection-specific rule beats a wildcard one within the same scope.
// RatePercent xor FixedPrice; a fixed price (the legacy "super" absolute override) is only valid at
// ElementPriceGroup scope. Rules are commercial terms, NOT catalogue content: unversioned, and rule
// edits never touch existing order lines (the stored line outcome is the pin).
public class DiscountRule
{
    public int Id { get; set; }
    public int SellerId { get; set; }
    public string? CollectionCode { get; set; }
    public DiscountScope Scope { get; set; }
    public string? ElementCode { get; set; }
    public string? PriceGroupCode { get; set; }
    public string? ModelCode { get; set; }
    public ModelType? ModelType { get; set; }
    public string? MaterialTypeCode { get; set; }
    public decimal? RatePercent { get; set; }
    public decimal? FixedPrice { get; set; }
}
