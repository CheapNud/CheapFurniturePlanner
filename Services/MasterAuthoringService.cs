using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Services;

// Edits the seven flat pricing masters in the working masters document: load the full authoring
// snapshot, mutate the target master list, persist via SaveMastersAsync (masters only). NEVER
// republishes — edited masters reach the planner only when P1 publishes a new dated version.
// Codes are immutable: every Update matches the existing row by its identity key and replaces the
// scalars, pinning the key. Delete is guarded against references in MasterReferenceScanner (Task 2).
public sealed class MasterAuthoringService(AuthoringCatalogueStore store)
{
    // --- Material (identity: Code) ---
    public Task AddMaterialAsync(Material material, CancellationToken ct = default) => MutateAsync(s =>
    {
        RequireUnique(s.Materials.Select(m => m.Code), material.Code, "Material");
        RequireNonNegative(material.UnitCost, "Unit cost");
        s.Materials.Add(material with { Code = material.Code.Trim() });
    }, ct);

    public Task UpdateMaterialAsync(string code, Material material, CancellationToken ct = default) => MutateAsync(s =>
    {
        var index = RequireIndex(s.Materials.FindIndex(m => m.Code == code), "Material", code);
        RequireNonNegative(material.UnitCost, "Unit cost");
        s.Materials[index] = material with { Code = code };
    }, ct);

    public Task DeleteMaterialAsync(string code, CancellationToken ct = default) => MutateAsync(s =>
    {
        Guard(s, MasterKind.Material, code);
        s.Materials.RemoveAll(m => m.Code == code);
    }, ct);

    // --- Operation (identity: Code) ---
    public Task AddOperationAsync(Operation operation, CancellationToken ct = default) => MutateAsync(s =>
    {
        RequireUnique(s.Operations.Select(o => o.Code), operation.Code, "Operation");
        RequireNonNegative(operation.UnitCost, "Unit cost");
        s.Operations.Add(operation with { Code = operation.Code.Trim() });
    }, ct);

    public Task UpdateOperationAsync(string code, Operation operation, CancellationToken ct = default) => MutateAsync(s =>
    {
        var index = RequireIndex(s.Operations.FindIndex(o => o.Code == code), "Operation", code);
        RequireNonNegative(operation.UnitCost, "Unit cost");
        s.Operations[index] = operation with { Code = code };
    }, ct);

    public Task DeleteOperationAsync(string code, CancellationToken ct = default) => MutateAsync(s =>
    {
        Guard(s, MasterKind.Operation, code);
        s.Operations.RemoveAll(o => o.Code == code);
    }, ct);

    // --- FrameBody (identity: Code) ---
    public Task AddFrameBodyAsync(FrameBody frameBody, CancellationToken ct = default) => MutateAsync(s =>
    {
        RequireUnique(s.FrameBodies.Select(f => f.Code), frameBody.Code, "Frame body");
        RequireFrameBodyNonNegative(frameBody);
        s.FrameBodies.Add(frameBody with { Code = frameBody.Code.Trim() });
    }, ct);

    public Task UpdateFrameBodyAsync(string code, FrameBody frameBody, CancellationToken ct = default) => MutateAsync(s =>
    {
        var index = RequireIndex(s.FrameBodies.FindIndex(f => f.Code == code), "Frame body", code);
        RequireFrameBodyNonNegative(frameBody);
        s.FrameBodies[index] = frameBody with { Code = code };
    }, ct);

    public Task DeleteFrameBodyAsync(string code, CancellationToken ct = default) => MutateAsync(s =>
    {
        Guard(s, MasterKind.FrameBody, code);
        s.FrameBodies.RemoveAll(f => f.Code == code);
    }, ct);

    // --- SprayPrice (identity: FrameBodyCode; must reference an existing FrameBody) ---
    public Task AddSprayPriceAsync(SprayPrice sprayPrice, CancellationToken ct = default) => MutateAsync(s =>
    {
        RequireExists(s.FrameBodies.Select(f => f.Code), sprayPrice.FrameBodyCode, "Frame body");
        RequireUnique(s.SprayPrices.Select(p => p.FrameBodyCode), sprayPrice.FrameBodyCode, "Spray price for frame body");
        RequireNonNegative(sprayPrice.Price, "Price");
        s.SprayPrices.Add(sprayPrice);
    }, ct);

    public Task UpdateSprayPriceAsync(string frameBodyCode, SprayPrice sprayPrice, CancellationToken ct = default) => MutateAsync(s =>
    {
        var index = RequireIndex(s.SprayPrices.FindIndex(p => p.FrameBodyCode == frameBodyCode), "Spray price for frame body", frameBodyCode);
        RequireNonNegative(sprayPrice.Price, "Price");
        s.SprayPrices[index] = sprayPrice with { FrameBodyCode = frameBodyCode };
    }, ct);

    public Task DeleteSprayPriceAsync(string frameBodyCode, CancellationToken ct = default) => MutateAsync(s =>
    {
        s.SprayPrices.RemoveAll(p => p.FrameBodyCode == frameBodyCode);
    }, ct);

    // --- PriceGroup (class; identity: Code; Id preserved on update) ---
    public Task AddPriceGroupAsync(PriceGroup priceGroup, CancellationToken ct = default) => MutateAsync(s =>
    {
        RequireUnique(s.PriceGroups.Select(p => p.Code), priceGroup.Code, "Price group");
        RequireNonNegative(priceGroup.RatePerMeter, "Rate per meter");
        priceGroup.Code = priceGroup.Code.Trim();
        s.PriceGroups.Add(priceGroup);
    }, ct);

    public Task UpdatePriceGroupAsync(string code, PriceGroup priceGroup, CancellationToken ct = default) => MutateAsync(s =>
    {
        var existing = s.PriceGroups.FirstOrDefault(p => p.Code == code)
            ?? throw new InvalidOperationException($"Price group '{code}' not found.");
        RequireNonNegative(priceGroup.RatePerMeter, "Rate per meter");
        existing.Kind = priceGroup.Kind;
        existing.RatePerMeter = priceGroup.RatePerMeter;
        existing.MaterialTypeCode = priceGroup.MaterialTypeCode;
    }, ct);

    public Task DeletePriceGroupAsync(string code, CancellationToken ct = default) => MutateAsync(s =>
    {
        Guard(s, MasterKind.PriceGroup, code);
        s.PriceGroups.RemoveAll(p => p.Code == code);
    }, ct);

    // --- FixedSurcharge (identity: Name) ---
    public Task AddFixedSurchargeAsync(FixedSurcharge surcharge, CancellationToken ct = default) => MutateAsync(s =>
    {
        RequireUnique(s.FixedSurcharges.Select(f => f.Name), surcharge.Name, "Fixed surcharge");
        RequireNonNegative(surcharge.Amount, "Amount");
        s.FixedSurcharges.Add(surcharge with { Name = surcharge.Name.Trim() });
    }, ct);

    public Task UpdateFixedSurchargeAsync(string name, FixedSurcharge surcharge, CancellationToken ct = default) => MutateAsync(s =>
    {
        var index = RequireIndex(s.FixedSurcharges.FindIndex(f => f.Name == name), "Fixed surcharge", name);
        RequireNonNegative(surcharge.Amount, "Amount");
        s.FixedSurcharges[index] = surcharge with { Name = name };
    }, ct);

    public Task DeleteFixedSurchargeAsync(string name, CancellationToken ct = default) => MutateAsync(s =>
    {
        s.FixedSurcharges.RemoveAll(f => f.Name == name);
    }, ct);

    // --- ChoiceSurcharge (identity: OptionChoiceCode + ElementCode) ---
    public Task AddChoiceSurchargeAsync(ChoiceSurcharge surcharge, CancellationToken ct = default) => MutateAsync(s =>
    {
        if (s.ChoiceSurcharges.Any(c => c.OptionChoiceCode == surcharge.OptionChoiceCode && c.ElementCode == surcharge.ElementCode))
        {
            throw new InvalidOperationException($"Choice surcharge for '{surcharge.OptionChoiceCode}' / '{surcharge.ElementCode ?? "(any element)"}' already exists.");
        }
        RequireNonNegative(surcharge.Amount, "Amount");
        s.ChoiceSurcharges.Add(surcharge);
    }, ct);

    public Task UpdateChoiceSurchargeAsync(string optionChoiceCode, string? elementCode, ChoiceSurcharge surcharge, CancellationToken ct = default) => MutateAsync(s =>
    {
        var index = s.ChoiceSurcharges.FindIndex(c => c.OptionChoiceCode == optionChoiceCode && c.ElementCode == elementCode);
        if (index < 0) { throw new InvalidOperationException($"Choice surcharge for '{optionChoiceCode}' / '{elementCode ?? "(any element)"}' not found."); }
        RequireNonNegative(surcharge.Amount, "Amount");
        s.ChoiceSurcharges[index] = surcharge with { OptionChoiceCode = optionChoiceCode, ElementCode = elementCode };
    }, ct);

    public Task DeleteChoiceSurchargeAsync(string optionChoiceCode, string? elementCode, CancellationToken ct = default) => MutateAsync(s =>
    {
        s.ChoiceSurcharges.RemoveAll(c => c.OptionChoiceCode == optionChoiceCode && c.ElementCode == elementCode);
    }, ct);

    // --- shared plumbing ---
    private async Task MutateAsync(Action<CatalogueSnapshot> mutate, CancellationToken ct)
    {
        var snapshot = await store.LoadAsync(ct);
        mutate(snapshot);
        await store.SaveMastersAsync(snapshot, ct);
    }

    private static void RequireUnique(IEnumerable<string> existing, string code, string label)
    {
        if (existing.Contains(code)) { throw new InvalidOperationException($"{label} '{code}' already exists."); }
    }

    private static void RequireExists(IEnumerable<string> valid, string code, string label)
    {
        if (!valid.Contains(code)) { throw new InvalidOperationException($"{label} '{code}' does not exist."); }
    }

    private static void RequireNonNegative(decimal amount, string label)
    {
        if (amount < 0) { throw new InvalidOperationException($"{label} cannot be negative."); }
    }

    private static int RequireIndex(int index, string label, string code)
        => index >= 0 ? index : throw new InvalidOperationException($"{label} '{code}' not found.");

    private static void Guard(CatalogueSnapshot snapshot, MasterKind kind, string code)
    {
        var references = MasterReferenceScanner.FindReferences(snapshot, kind, code);
        if (references.Count > 0) { throw new MasterReferencedException(kind, code, references); }
    }

    private static void RequireFrameBodyNonNegative(FrameBody frameBody)
    {
        RequireNonNegative(frameBody.Price, "Price");
        RequireNonNegative(frameBody.UnitCost, "Unit cost");
        RequireNonNegative(frameBody.MachiningUnits, "Machining units");
        RequireNonNegative(frameBody.RefiningUnits, "Refining units");
    }
}
