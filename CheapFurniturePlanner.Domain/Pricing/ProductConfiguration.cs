namespace CheapFurniturePlanner.Domain.Pricing;

public record ProductConfiguration(string ModelCode, IReadOnlyList<ElementSelection> Selections);

public record ElementSelection(string ElementCode, int Quantity, IReadOnlyDictionary<string, string> ChoiceSelections, string? FabricColorCode);
