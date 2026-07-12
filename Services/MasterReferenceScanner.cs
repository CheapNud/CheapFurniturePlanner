using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Services;

public enum MasterKind { Material, Operation, FrameBody, PriceGroup, SprayPrice, FixedSurcharge, ChoiceSurcharge }

// Pure scan of a catalogue snapshot for everything that references a given master, so the authoring
// service can block deletion of a still-used master. Static (stateless pure function) — no DI. Only
// Material/Operation/FrameBody/PriceGroup are referenced by anything; the other three return empty.
public static class MasterReferenceScanner
{
    public static IReadOnlyList<string> FindReferences(CatalogueSnapshot snapshot, MasterKind kind, string code)
    {
        var references = new List<string>();
        switch (kind)
        {
            case MasterKind.Material:
                foreach (var (model, element, line) in Lines(snapshot))
                {
                    var used = line switch
                    {
                        FoamBomLine foam => foam.FoamCode == code,
                        CottonBomLine cotton => cotton.CottonQualityCode == code,
                        MiscBomLine misc => misc.MaterialCode == code,
                        _ => false
                    };
                    if (used) { references.Add($"{model.Code}/{element.Code} BOM line '{line.LineKey}'"); }
                }
                foreach (var (model, element) in Elements(snapshot))
                {
                    foreach (var sub in element.Substitutions.Where(su => su.ReplaceMaterialCode == code || su.WithMaterialCode == code))
                    {
                        references.Add($"{model.Code}/{element.Code} substitution ({sub.ReplaceMaterialCode}→{sub.WithMaterialCode})");
                    }
                }
                break;

            case MasterKind.Operation:
                foreach (var (model, element, line) in Lines(snapshot))
                {
                    if (line is LaborBomLine labor && labor.OperationCode == code)
                    {
                        references.Add($"{model.Code}/{element.Code} BOM line '{line.LineKey}'");
                    }
                }
                break;

            case MasterKind.FrameBody:
                foreach (var (model, element, line) in Lines(snapshot))
                {
                    if (line is FrameBomLine frame && frame.FrameBodyCode == code)
                    {
                        references.Add($"{model.Code}/{element.Code} BOM line '{line.LineKey}'");
                    }
                }
                if (snapshot.SprayPrices.Any(p => p.FrameBodyCode == code))
                {
                    references.Add($"spray price for '{code}'");
                }
                break;

            case MasterKind.PriceGroup:
                foreach (var group in snapshot.FabricGroups.Where(g => g.PriceGroupCode == code))
                {
                    references.Add($"fabric group '{group.Code}'");
                }
                break;

            // SprayPrice, FixedSurcharge, ChoiceSurcharge are referenced by nothing.
            case MasterKind.SprayPrice:
            case MasterKind.FixedSurcharge:
            case MasterKind.ChoiceSurcharge:
                break;
        }
        return references;
    }

    private static IEnumerable<(Domain.Catalog.FurnitureModel Model, Domain.Catalog.Element Element)> Elements(CatalogueSnapshot snapshot)
        => snapshot.Models.SelectMany(m => m.Elements.Select(e => (m, e)));

    private static IEnumerable<(Domain.Catalog.FurnitureModel Model, Domain.Catalog.Element Element, BomLine Line)> Lines(CatalogueSnapshot snapshot)
        => Elements(snapshot).SelectMany(pair => pair.Element.Bom.Sections.SelectMany(s => s.Lines).Select(line => (pair.Model, pair.Element, line)));
}

// Thrown when a delete is blocked because the master is still referenced. Message summarizes the
// first few referencing sites; References carries the full list for the UI.
public sealed class MasterReferencedException(MasterKind kind, string code, IReadOnlyList<string> references)
    : Exception(BuildMessage(kind, code, references))
{
    public IReadOnlyList<string> References { get; } = references;

    private static string BuildMessage(MasterKind kind, string code, IReadOnlyList<string> references)
    {
        var shown = string.Join("; ", references.Take(5));
        var more = references.Count > 5 ? "; …" : "";
        return $"Cannot delete {kind} '{code}': referenced by {references.Count} item(s): {shown}{more}.";
    }
}
