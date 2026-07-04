namespace CheapFurniturePlanner.Domain.Pricing;

public record MarkupTraceEntry(string StepName, decimal Percent, string Mode, decimal ResultAfter);

public record ElementBreakdown(
    string ElementCode,
    string VariantCode,
    int Quantity,
    IReadOnlyList<BreakdownLine> Lines,
    IReadOnlyDictionary<string, decimal> StageSubtotals,
    IReadOnlyList<MarkupTraceEntry> MarkupTrace,
    decimal ElementTotal);

public record PriceBreakdown(
    string CatalogueVersion,
    string ContentHash,
    string MarketCode,
    IReadOnlyList<ElementBreakdown> Elements,
    decimal DocumentTotal);
