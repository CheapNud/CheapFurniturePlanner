namespace CheapFurniturePlanner.Domain.Bom;

public record ApplicabilityCondition(IReadOnlyList<SelectionKey> RequiredSelections)
{
    public bool IsSatisfiedBy(IReadOnlyDictionary<string, string> selections) =>
        RequiredSelections.All(rs => selections.TryGetValue(rs.OptionDefinitionCode, out var chosen) && chosen == rs.ChoiceCode);
}

public record SelectionKey(string OptionDefinitionCode, string ChoiceCode);
