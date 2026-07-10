using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Draft-only authoring of an element's option list (ChoiceOption/FabricOption), persisted through the
// authoring store. Never republishes (Draft models are absent from the Active-only published snapshot).
// Prunes the element's stranded VariantNaming rows only when a BOM-significant change alters the
// element's enumerated variant space: VariantCode depends solely on which AffectsBom ChoiceOptions
// exist and their choice codes (it sorts segments, so option/value order, defaults, Required, and
// FabricOptions never affect it).
public sealed class OptionAuthoringService(IDbContextFactory<FurniturePlannerContext> factory, AuthoringCatalogueStore store, ModelPublishService publish)
{
    private static readonly char[] ForbiddenCodeChars = [':', '-'];   // VariantCode delimiters

    public async Task AddOptionAsync(string modelCode, string elementCode, ProductOption option, CancellationToken ct = default)
    {
        var (model, element) = await LoadDraftElementAsync(modelCode, elementCode, ct);
        var defCode = await ValidateOptionAsync(element, option, originalDefCode: null, ct);
        option.OptionDefinitionCode = defCode;
        option.DisplayIndex = element.Options.Count;
        element.Options.Add(option);
        if (IsBomSignificant(option)) { await PruneNamingRowsAsync(modelCode, elementCode, ct); }
        await store.SaveModelAsync(model, ct);
    }

    public async Task UpdateOptionAsync(string modelCode, string elementCode, string originalDefCode, ProductOption option, CancellationToken ct = default)
    {
        var (model, element) = await LoadDraftElementAsync(modelCode, elementCode, ct);
        var index = element.Options.FindIndex(o => o.OptionDefinitionCode == originalDefCode);
        if (index < 0) { throw new InvalidOperationException($"Option '{originalDefCode}' not found on element '{elementCode}'."); }
        var existing = element.Options[index];
        var defCode = await ValidateOptionAsync(element, option, originalDefCode, ct);
        option.OptionDefinitionCode = defCode;
        option.VisibilityRules = existing.VisibilityRules;   // 8B authors these; preserve across edits
        option.DisplayIndex = existing.DisplayIndex;
        var mustPrune = PruneRequiredForUpdate(existing, option);
        element.Options[index] = option;
        if (mustPrune) { await PruneNamingRowsAsync(modelCode, elementCode, ct); }
        await store.SaveModelAsync(model, ct);
    }

    public async Task RemoveOptionAsync(string modelCode, string elementCode, string defCode, CancellationToken ct = default)
    {
        var (model, element) = await LoadDraftElementAsync(modelCode, elementCode, ct);
        var existing = element.Options.FirstOrDefault(o => o.OptionDefinitionCode == defCode)
            ?? throw new InvalidOperationException($"Option '{defCode}' not found on element '{elementCode}'.");
        element.Options.Remove(existing);
        RenumberOptions(element);
        if (IsBomSignificant(existing)) { await PruneNamingRowsAsync(modelCode, elementCode, ct); }
        await store.SaveModelAsync(model, ct);
    }

    public async Task ReorderOptionsAsync(string modelCode, string elementCode, IReadOnlyList<string> orderedDefCodes, CancellationToken ct = default)
    {
        var (model, element) = await LoadDraftElementAsync(modelCode, elementCode, ct);
        var currentCodes = element.Options.Select(o => o.OptionDefinitionCode).ToHashSet();
        if (orderedDefCodes.Count != element.Options.Count || !orderedDefCodes.ToHashSet().SetEquals(currentCodes))
        {
            throw new InvalidOperationException($"Reorder for element '{elementCode}' must be a permutation of its option def codes.");
        }
        element.Options = orderedDefCodes.Select(orderedCode => element.Options.First(o => o.OptionDefinitionCode == orderedCode)).ToList();
        RenumberOptions(element);
        await store.SaveModelAsync(model, ct);   // order does not affect VariantCode -> no prune
    }

    private async Task<(FurnitureModel Model, Element Element)> LoadDraftElementAsync(string modelCode, string elementCode, CancellationToken ct)
    {
        var model = await store.LoadModelAsync(modelCode, ct)
            ?? throw new InvalidOperationException($"Model '{modelCode}' not found.");
        if (await publish.GetStateAsync(modelCode, ct) != TradeItemState.Draft)
        {
            throw new StructureFrozenException(modelCode);
        }
        var element = model.Elements.FirstOrDefault(e => e.Code == elementCode)
            ?? throw new InvalidOperationException($"Element '{elementCode}' not found in model '{modelCode}'.");
        return (model, element);
    }

    // Validates the option, trims codes in place, returns the trimmed def code. originalDefCode null =
    // add (reject any existing def code); else update (reject collision with a DIFFERENT option).
    private async Task<string> ValidateOptionAsync(Element element, ProductOption option, string? originalDefCode, CancellationToken ct)
    {
        var defCode = option.OptionDefinitionCode?.Trim() ?? string.Empty;
        RequireCode(defCode, "Option definition code");
        if (defCode == VariantCode.MaterialDefCode)
        {
            throw new InvalidOperationException($"Option definition code '{defCode}' is reserved.");
        }
        if (element.Options.Any(o => o.OptionDefinitionCode == defCode && o.OptionDefinitionCode != originalDefCode))
        {
            throw new InvalidOperationException($"Option definition code '{defCode}' already exists on this element.");
        }
        switch (option)
        {
            case ChoiceOption choice:
                if (choice.Values.Count == 0) { throw new InvalidOperationException($"Choice option '{defCode}' must have at least one value."); }
                var seen = new HashSet<string>();
                foreach (var choiceValue in choice.Values)
                {
                    var choiceCode = choiceValue.OptionChoiceCode?.Trim() ?? string.Empty;
                    RequireCode(choiceCode, "Option choice code");
                    if (!seen.Add(choiceCode)) { throw new InvalidOperationException($"Duplicate choice code '{choiceCode}' in option '{defCode}'."); }
                    choiceValue.OptionChoiceCode = choiceCode;
                }
                if (choice.Values.Count(v => v.IsDefault) > 1) { throw new InvalidOperationException($"Choice option '{defCode}' has more than one default value."); }
                break;
            case FabricOption fabric:
                var validGroups = (await store.LoadAsync(ct)).FabricGroups.Select(g => g.Code).ToHashSet();
                var unknown = fabric.FabricGroupCodes.FirstOrDefault(fg => !validGroups.Contains(fg));
                if (unknown is not null) { throw new InvalidOperationException($"Fabric group '{unknown}' does not exist."); }
                break;
        }
        return defCode;
    }

    private static bool IsBomSignificant(ProductOption option) => option is ChoiceOption { AffectsBom: true };

    // VariantCode changes only if an AffectsBom ChoiceOption's presence, def code, AffectsBom flag, or
    // value-code SET changes. So prune iff at least one side is BOM-significant AND something in that
    // set changed (toggle, def-code rename, or a different value-code set).
    private static bool PruneRequiredForUpdate(ProductOption existing, ProductOption incoming)
    {
        var existingBom = IsBomSignificant(existing);
        var incomingBom = IsBomSignificant(incoming);
        if (!existingBom && !incomingBom) { return false; }
        if (existingBom != incomingBom) { return true; }                                   // AffectsBom toggled
        if (existing.OptionDefinitionCode != incoming.OptionDefinitionCode) { return true; } // def code renamed
        var existingCodes = ((ChoiceOption)existing).Values.Select(v => v.OptionChoiceCode).ToHashSet();
        var incomingCodes = ((ChoiceOption)incoming).Values.Select(v => v.OptionChoiceCode).ToHashSet();
        return !existingCodes.SetEquals(incomingCodes);                                     // value-code set changed
    }

    private async Task PruneNamingRowsAsync(string modelCode, string elementCode, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var prefix = elementCode + "-";
        await db.VariantNamings
            .Where(n => n.ModelCode == modelCode && (n.VariantCode == elementCode || n.VariantCode.StartsWith(prefix)))
            .ExecuteDeleteAsync(ct);
    }

    private static void RenumberOptions(Element element)
    {
        for (var index = 0; index < element.Options.Count; index++)
        {
            element.Options[index].DisplayIndex = index;
        }
    }

    private static void RequireCode(string code, string label)
    {
        if (string.IsNullOrEmpty(code)) { throw new InvalidOperationException($"{label} is required."); }
        if (code.IndexOfAny(ForbiddenCodeChars) >= 0) { throw new InvalidOperationException($"{label} cannot contain ':' or '-'."); }
    }
}
