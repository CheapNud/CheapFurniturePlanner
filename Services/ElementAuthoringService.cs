using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Domain.Catalog;

namespace CheapFurniturePlanner.Services;

public sealed class StructureFrozenException(string modelCode)
    : Exception($"Model '{modelCode}' is not in Draft; structure is frozen.");

// Draft-only authoring of a model's element list, persisted through the authoring document store.
// Never republishes: structure edits are gated to Draft, and Draft models are absent from the
// published (Active-only) snapshot the planner reads. Renaming/removing an element prunes its now-
// stranded catalogue-backed articles, whose VariantCode is prefixed with the element's code.
public sealed class ElementAuthoringService(AuthoringCatalogueStore store, ModelPublishService publish, ArticleAuthoringService articles)
{
    public async Task AddElementAsync(string modelCode, Element element, CancellationToken ct = default)
    {
        var model = await LoadDraftModelAsync(modelCode, ct);
        var elementCode = element.Code?.Trim() ?? string.Empty;
        var elementName = element.Name?.Trim() ?? string.Empty;
        RequireCodeAndName(elementCode, elementName);
        if (model.Elements.Any(existing => existing.Code == elementCode))
        {
            throw new InvalidOperationException($"Element code '{elementCode}' already exists in model '{modelCode}'.");
        }
        element.Code = elementCode;
        element.Name = elementName;
        element.DisplayIndex = model.Elements.Count;
        model.Elements.Add(element);
        await store.SaveModelAsync(model, ct);
    }

    public async Task UpdateElementAsync(string modelCode, string originalCode, Element element, CancellationToken ct = default)
    {
        var model = await LoadDraftModelAsync(modelCode, ct);
        var target = model.Elements.FirstOrDefault(existing => existing.Code == originalCode)
            ?? throw new InvalidOperationException($"Element '{originalCode}' not found in model '{modelCode}'.");
        var elementCode = element.Code?.Trim() ?? string.Empty;
        var elementName = element.Name?.Trim() ?? string.Empty;
        RequireCodeAndName(elementCode, elementName);
        if (elementCode != originalCode)
        {
            if (model.Elements.Any(existing => existing.Code == elementCode))
            {
                throw new InvalidOperationException($"Element code '{elementCode}' already exists in model '{modelCode}'.");
            }
            await PruneNamingRowsAsync(modelCode, originalCode, ct);
        }
        // Scalars only — Options/Bom/Substitutions/DisplayIndex on the passed element are ignored,
        // the target's existing structure is preserved.
        target.Code = elementCode;
        target.Name = elementName;
        target.Width = element.Width;
        target.Depth = element.Depth;
        target.Height = element.Height;
        target.TransportUnits = element.TransportUnits;
        await store.SaveModelAsync(model, ct);
    }

    public async Task RemoveElementAsync(string modelCode, string elementCode, CancellationToken ct = default)
    {
        var model = await LoadDraftModelAsync(modelCode, ct);
        var target = model.Elements.FirstOrDefault(existing => existing.Code == elementCode)
            ?? throw new InvalidOperationException($"Element '{elementCode}' not found in model '{modelCode}'.");
        model.Elements.Remove(target);
        Renumber(model);
        await PruneNamingRowsAsync(modelCode, elementCode, ct);
        await store.SaveModelAsync(model, ct);
    }

    public async Task ReorderElementsAsync(string modelCode, IReadOnlyList<string> orderedCodes, CancellationToken ct = default)
    {
        var model = await LoadDraftModelAsync(modelCode, ct);
        var currentCodes = model.Elements.Select(existing => existing.Code).ToHashSet();
        if (orderedCodes.Count != model.Elements.Count || !orderedCodes.ToHashSet().SetEquals(currentCodes))
        {
            throw new InvalidOperationException($"Reorder for '{modelCode}' must be a permutation of its element codes.");
        }
        model.Elements = orderedCodes.Select(orderedCode => model.Elements.First(existing => existing.Code == orderedCode)).ToList();
        Renumber(model);
        await store.SaveModelAsync(model, ct);
    }

    private async Task<FurnitureModel> LoadDraftModelAsync(string modelCode, CancellationToken ct)
    {
        var model = await store.LoadModelAsync(modelCode, ct)
            ?? throw new InvalidOperationException($"Model '{modelCode}' not found.");
        if (await publish.GetStateAsync(modelCode, ct) != TradeItemState.Draft)
        {
            throw new StructureFrozenException(modelCode);
        }
        return model;
    }

    private async Task PruneNamingRowsAsync(string modelCode, string elementCode, CancellationToken ct)
        => await articles.PruneForElementAsync(modelCode, elementCode, ct);

    private static void Renumber(FurnitureModel model)
    {
        for (var index = 0; index < model.Elements.Count; index++)
        {
            model.Elements[index].DisplayIndex = index;
        }
    }

    private static void RequireCodeAndName(string elementCode, string elementName)
    {
        if (string.IsNullOrEmpty(elementCode)) { throw new InvalidOperationException("Element code is required."); }
        if (string.IsNullOrEmpty(elementName)) { throw new InvalidOperationException("Element name is required."); }
        if (elementCode.Contains('-')) { throw new InvalidOperationException("Element code cannot contain '-'."); }
    }
}
