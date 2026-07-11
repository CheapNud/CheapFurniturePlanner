using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Draft-only authoring of an element's BOM structure (the six BomLine kinds), persisted through the
// authoring store. NO pruning (VariantCode is derived from AffectsBom options, never the BOM) and
// never republishes (Draft models are absent from the Active-only published snapshot). Sections are
// auto-managed: a BomSection exists for a kind exactly when it has >= 1 line. Line records are
// immutable; updates rebuild via `with`, pinning the stable LineKey. Each line's Condition is
// authored by the caller and validated against the element's choice options.
public sealed class BomAuthoringService(IDbContextFactory<FurniturePlannerContext> factory, AuthoringCatalogueStore store, ModelPublishService publish)
{
    public async Task AddLineAsync(string modelCode, string elementCode, BomSectionKind kind, BomLine line, CancellationToken ct = default)
    {
        var (model, element) = await LoadDraftElementAsync(modelCode, elementCode, ct);
        var lineKey = await ValidateLineAsync(element, kind, line, isAdd: true, ct);
        var section = element.Bom.Sections.FirstOrDefault(s => s.Kind == kind);
        if (section is null)
        {
            section = new BomSection { Kind = kind };
            element.Bom.Sections.Add(section);
        }
        section.Lines.Add(line with { LineKey = lineKey, Condition = line.Condition });
        await store.SaveModelAsync(model, ct);
    }

    public async Task UpdateLineAsync(string modelCode, string elementCode, string lineKey, BomLine line, CancellationToken ct = default)
    {
        var (model, element) = await LoadDraftElementAsync(modelCode, elementCode, ct);
        var located = LocateLine(element, lineKey)
            ?? throw new InvalidOperationException($"BOM line '{lineKey}' not found on element '{elementCode}'.");
        await ValidateLineAsync(element, located.Section.Kind, line, isAdd: false, ct);
        located.Section.Lines[located.Index] = line with { LineKey = lineKey, Condition = line.Condition };
        await store.SaveModelAsync(model, ct);
    }

    public async Task RemoveLineAsync(string modelCode, string elementCode, string lineKey, CancellationToken ct = default)
    {
        var (model, element) = await LoadDraftElementAsync(modelCode, elementCode, ct);
        var located = LocateLine(element, lineKey)
            ?? throw new InvalidOperationException($"BOM line '{lineKey}' not found on element '{elementCode}'.");
        located.Section.Lines.RemoveAt(located.Index);
        if (located.Section.Lines.Count == 0) { element.Bom.Sections.Remove(located.Section); }
        await store.SaveModelAsync(model, ct);
    }

    public async Task ReorderLinesAsync(string modelCode, string elementCode, BomSectionKind kind, IReadOnlyList<string> orderedLineKeys, CancellationToken ct = default)
    {
        var (model, element) = await LoadDraftElementAsync(modelCode, elementCode, ct);
        var section = element.Bom.Sections.FirstOrDefault(s => s.Kind == kind)
            ?? throw new InvalidOperationException($"Element '{elementCode}' has no {kind} BOM section.");
        var currentKeys = section.Lines.Select(l => l.LineKey).ToHashSet();
        if (orderedLineKeys.Count != section.Lines.Count || !orderedLineKeys.ToHashSet().SetEquals(currentKeys))
        {
            throw new InvalidOperationException($"Reorder for the {kind} section of '{elementCode}' must be a permutation of its line keys.");
        }
        var reordered = orderedLineKeys.Select(key => section.Lines.First(l => l.LineKey == key)).ToList();
        section.Lines.Clear();
        section.Lines.AddRange(reordered);
        await store.SaveModelAsync(model, ct);
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

    private static (BomSection Section, int Index, BomLine Line)? LocateLine(Element element, string lineKey)
    {
        foreach (var section in element.Bom.Sections)
        {
            var index = section.Lines.FindIndex(l => l.LineKey == lineKey);
            if (index >= 0) { return (section, index, section.Lines[index]); }
        }
        return null;
    }

    // Validates the line, returns the trimmed LineKey. isAdd=true rejects a duplicate key anywhere in
    // the element's BOM; on update the key is located separately and unchanged.
    private async Task<string> ValidateLineAsync(Element element, BomSectionKind kind, BomLine line, bool isAdd, CancellationToken ct)
    {
        var lineKey = line.LineKey?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(lineKey)) { throw new InvalidOperationException("BOM line key is required."); }
        if (isAdd && element.Bom.Sections.SelectMany(s => s.Lines).Any(l => l.LineKey == lineKey))
        {
            throw new InvalidOperationException($"BOM line key '{lineKey}' already exists on this element.");
        }
        RequireKindMatch(kind, line);
        RequireNonNegative(line);
        ValidateReferences(line, await store.LoadAsync(ct));
        ValidateCondition(element, line.Condition);
        return lineKey;
    }

    private static void ValidateCondition(Element element, ApplicabilityCondition? condition)
    {
        if (condition is null) { return; }
        var seenOptions = new HashSet<string>();
        foreach (var key in condition.RequiredSelections)
        {
            if (!seenOptions.Add(key.OptionDefinitionCode))
            {
                throw new InvalidOperationException($"Condition references option '{key.OptionDefinitionCode}' more than once.");
            }
            if (element.Options.FirstOrDefault(o => o.OptionDefinitionCode == key.OptionDefinitionCode) is not ChoiceOption trigger)
            {
                throw new InvalidOperationException($"Condition references unknown option '{key.OptionDefinitionCode}'.");
            }
            if (!trigger.Values.Any(v => v.OptionChoiceCode == key.ChoiceCode))
            {
                throw new InvalidOperationException($"Option '{key.OptionDefinitionCode}' has no choice '{key.ChoiceCode}'.");
            }
        }
    }

    private static void RequireKindMatch(BomSectionKind kind, BomLine line)
    {
        var matches = kind switch
        {
            BomSectionKind.Frame => line is FrameBomLine,
            BomSectionKind.Foam => line is FoamBomLine,
            BomSectionKind.Cotton => line is CottonBomLine,
            BomSectionKind.CutSort => line is CutSortBomLine,
            BomSectionKind.Misc => line is MiscBomLine,
            BomSectionKind.Labor => line is LaborBomLine,
            _ => false
        };
        if (!matches) { throw new InvalidOperationException($"Line type does not match section kind '{kind}'."); }
    }

    private static void RequireNonNegative(BomLine line)
    {
        if (line.Quantity < 0) { throw new InvalidOperationException("Quantity cannot be negative."); }
        var bad = line switch
        {
            CottonBomLine cotton => cotton.Measurement < 0 || cotton.CutUnits < 0 || cotton.UnitConversionFactor < 0,
            CutSortBomLine cutsort => cutsort.Metrage < 0 || cutsort.CutUnits < 0 || cutsort.SecondaryGroupMetrages.Values.Any(v => v < 0),
            MiscBomLine misc => misc.UnitConversionFactor < 0,
            LaborBomLine labor => labor.Units < 0,
            _ => false
        };
        if (bad) { throw new InvalidOperationException("BOM line values cannot be negative."); }
    }

    private static void ValidateReferences(BomLine line, CatalogueSnapshot snapshot)
    {
        switch (line)
        {
            case FrameBomLine frame:
                RequireCode(frame.FrameBodyCode, snapshot.FrameBodies.Select(f => f.Code), "Frame body");
                break;
            case FoamBomLine foam:
                RequireCode(foam.FoamCode, snapshot.Materials.Select(m => m.Code), "Foam material");
                break;
            case CottonBomLine cotton:
                RequireCode(cotton.CottonQualityCode, snapshot.Materials.Select(m => m.Code), "Cotton material");
                break;
            case MiscBomLine misc:
                RequireCode(misc.MaterialCode, snapshot.Materials.Select(m => m.Code), "Material");
                break;
            case LaborBomLine labor:
                RequireCode(labor.OperationCode, snapshot.Operations.Select(o => o.Code), "Operation");
                break;
            case CutSortBomLine cutsort:
                var groups = snapshot.FabricGroups.Select(g => g.Code).ToHashSet();
                var unknown = cutsort.SecondaryGroupMetrages.Keys.FirstOrDefault(k => !groups.Contains(k));
                if (unknown is not null) { throw new InvalidOperationException($"Fabric group '{unknown}' does not exist."); }
                break;
        }
    }

    private static void RequireCode(string code, IEnumerable<string> validCodes, string label)
    {
        if (!validCodes.Contains(code)) { throw new InvalidOperationException($"{label} '{code}' does not exist."); }
    }
}
