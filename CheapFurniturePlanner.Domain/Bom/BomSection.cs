namespace CheapFurniturePlanner.Domain.Bom;

public record BomSection
{
    public required BomSectionKind Kind { get; init; }
    public string? InheritsFromElementCode { get; init; }
    public List<BomLine> Lines { get; init; } = [];
}
