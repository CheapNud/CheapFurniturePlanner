namespace CheapFurniturePlanner.Domain.Production;

// One BOM-significant variant of an element: its composed VariantCode, the choice selections (plus the
// synthetic material segment) that produced it, and the resolved material type (null when the element
// has no visible fabric option for this combination).
public record VariantDescriptor(
    string VariantCode,
    IReadOnlyDictionary<string, string> BomSignificantSelections,
    string? MaterialTypeCode);
