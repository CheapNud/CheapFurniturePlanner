namespace CheapFurniturePlanner.Domain.Production;

public record ProductionIdentity(
    string ModelCode,
    string ElementCode,
    string VariantCode,
    string? SuggestedCode,
    string EffectiveCode,
    ProductionCodeStatus Status,
    bool IsExportable,
    string? MaterialTypeCode,
    IReadOnlyDictionary<string, string> BomSignificantSelections);
