namespace CheapFurniturePlanner.Models;

// Standalone market -> VAT-rate table, deliberately outside the versioned catalogue (rates are
// snapshotted onto invoice lines at issue time, so editing or deleting a rate never moves an
// issued invoice).
public class MarketVatRate
{
    public int Id { get; set; }
    public required string MarketCode { get; set; }
    public decimal RatePercent { get; set; }
}
