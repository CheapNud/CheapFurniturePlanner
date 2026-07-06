using CheapFurniturePlanner.Domain.Bom;

namespace CheapFurniturePlanner.Domain.Masters;

public record FixedSurcharge(string Name, BomSectionKind AppliesToSection, decimal Amount);
