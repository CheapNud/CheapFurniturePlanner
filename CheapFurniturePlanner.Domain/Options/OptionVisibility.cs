namespace CheapFurniturePlanner.Domain.Options;

internal static class OptionVisibility
{
    internal static bool IsVisible(ProductOption option, IReadOnlyDictionary<string, string> choiceSelections) =>
        option.VisibilityRules.Count == 0
        || option.VisibilityRules.Any(r => choiceSelections.TryGetValue(r.TriggerOptionDefinitionCode, out var chosen) && chosen == r.TriggerChoiceCode);
}
