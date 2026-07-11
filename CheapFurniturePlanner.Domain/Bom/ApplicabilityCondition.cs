namespace CheapFurniturePlanner.Domain.Bom;

public record ApplicabilityCondition(IReadOnlyList<SelectionKey> RequiredSelections)
{
    public bool IsSatisfiedBy(IReadOnlyDictionary<string, string> selections) =>
        RequiredSelections.All(rs => selections.TryGetValue(rs.OptionDefinitionCode, out var chosen) && chosen == rs.ChoiceCode);

    // Positional record default equality falls back to reference equality on the list (List<T> has no
    // value semantics), so two independently-deserialized copies of the same condition never compare
    // equal. Override with sequence equality since RequiredSelections is content, not identity.
    public virtual bool Equals(ApplicabilityCondition? other) =>
        other is not null && RequiredSelections.SequenceEqual(other.RequiredSelections);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var selection in RequiredSelections) { hash.Add(selection); }
        return hash.ToHashCode();
    }
}

public record SelectionKey(string OptionDefinitionCode, string ChoiceCode);
