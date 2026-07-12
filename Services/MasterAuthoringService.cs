using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Services;

// Edits the ten pricing masters (seven flat + three nested: fabric groups, combination rules,
// markets) in the working masters document: load the full authoring snapshot, mutate the target
// master list, persist via SaveMastersAsync (masters only). NEVER republishes — edited masters
// reach the planner only when P1 publishes a new dated version. Identities are immutable on
// update — matched by key (Code, or list index for combination price rules) and the scalars are
// replaced, pinning the key. Delete is guarded against references in MasterReferenceScanner (Task 2).
public sealed class MasterAuthoringService(AuthoringCatalogueStore store)
{
    // --- Material (identity: Code) ---
    public Task AddMaterialAsync(Material material, CancellationToken ct = default) => MutateAsync(s =>
    {
        var code = material.Code.Trim();
        RequireNonEmpty(code, "Material code");
        RequireUnique(s.Materials.Select(m => m.Code), code, "Material");
        RequireNonNegative(material.UnitCost, "Unit cost");
        s.Materials.Add(material with { Code = code });
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
        var code = operation.Code.Trim();
        RequireNonEmpty(code, "Operation code");
        RequireUnique(s.Operations.Select(o => o.Code), code, "Operation");
        RequireNonNegative(operation.UnitCost, "Unit cost");
        s.Operations.Add(operation with { Code = code });
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
        var code = frameBody.Code.Trim();
        RequireNonEmpty(code, "Frame body code");
        RequireUnique(s.FrameBodies.Select(f => f.Code), code, "Frame body");
        RequireFrameBodyNonNegative(frameBody);
        s.FrameBodies.Add(frameBody with { Code = code });
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
        var code = priceGroup.Code.Trim();
        RequireNonEmpty(code, "Price group code");
        RequireUnique(s.PriceGroups.Select(p => p.Code), code, "Price group");
        RequireNonNegative(priceGroup.RatePerMeter, "Rate per meter");
        priceGroup.Code = code;
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
        var name = surcharge.Name.Trim();
        RequireNonEmpty(name, "Fixed surcharge name");
        RequireUnique(s.FixedSurcharges.Select(f => f.Name), name, "Fixed surcharge");
        RequireNonNegative(surcharge.Amount, "Amount");
        s.FixedSurcharges.Add(surcharge with { Name = name });
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
        var optionChoiceCode = surcharge.OptionChoiceCode.Trim();
        RequireNonEmpty(optionChoiceCode, "Option choice code");
        if (s.ChoiceSurcharges.Any(c => c.OptionChoiceCode == optionChoiceCode && c.ElementCode == surcharge.ElementCode))
        {
            throw new InvalidOperationException($"Choice surcharge for '{optionChoiceCode}' / '{surcharge.ElementCode ?? "(any element)"}' already exists.");
        }
        RequireNonNegative(surcharge.Amount, "Amount");
        s.ChoiceSurcharges.Add(surcharge with { OptionChoiceCode = optionChoiceCode });
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

    // --- FabricGroup (class; identity: Code; Id preserved on update; PriceGroupCode must resolve) ---
    public Task AddFabricGroupAsync(FabricGroup fabricGroup, CancellationToken ct = default) => MutateAsync(s =>
    {
        var code = fabricGroup.Code.Trim();
        RequireNonEmpty(code, "Fabric group code");
        RequireUnique(s.FabricGroups.Select(g => g.Code), code, "Fabric group");
        RequireExists(s.PriceGroups.Select(p => p.Code), fabricGroup.PriceGroupCode, "Price group");
        NormalizeAndValidateColors(fabricGroup.Colors);
        fabricGroup.Code = code;
        s.FabricGroups.Add(fabricGroup);
    }, ct);

    public Task UpdateFabricGroupAsync(string code, FabricGroup fabricGroup, CancellationToken ct = default) => MutateAsync(s =>
    {
        var existing = s.FabricGroups.FirstOrDefault(g => g.Code == code)
            ?? throw new InvalidOperationException($"Fabric group '{code}' not found.");
        RequireExists(s.PriceGroups.Select(p => p.Code), fabricGroup.PriceGroupCode, "Price group");
        NormalizeAndValidateColors(fabricGroup.Colors);
        existing.PriceGroupCode = fabricGroup.PriceGroupCode;
        existing.Colors = fabricGroup.Colors;
    }, ct);

    public Task DeleteFabricGroupAsync(string code, CancellationToken ct = default) => MutateAsync(s =>
    {
        Guard(s, MasterKind.FabricGroup, code);
        s.FabricGroups.RemoveAll(g => g.Code == code);
    }, ct);

    // --- CombinationPriceRule (identity: list index; Adjustment may be negative) ---
    public Task AddCombinationPriceRuleAsync(CombinationPriceRule rule, CancellationToken ct = default) => MutateAsync(s =>
    {
        ValidateCombinationRule(rule);
        s.CombinationPriceRules.Add(rule);
    }, ct);

    public Task UpdateCombinationPriceRuleAsync(int index, CombinationPriceRule rule, CancellationToken ct = default) => MutateAsync(s =>
    {
        if (index < 0 || index >= s.CombinationPriceRules.Count) { throw new InvalidOperationException($"Combination price rule index {index} is out of range."); }
        ValidateCombinationRule(rule);
        s.CombinationPriceRules[index] = rule;
    }, ct);

    public Task RemoveCombinationPriceRuleAsync(int index, CancellationToken ct = default) => MutateAsync(s =>
    {
        if (index < 0 || index >= s.CombinationPriceRules.Count) { throw new InvalidOperationException($"Combination price rule index {index} is out of range."); }
        s.CombinationPriceRules.RemoveAt(index);
    }, ct);

    // --- MarketParameters (identity: Code; delete blocked when it is the last market) ---
    public Task AddMarketAsync(MarketParameters market, CancellationToken ct = default) => MutateAsync(s =>
    {
        var code = market.Code.Trim();
        RequireNonEmpty(code, "Market code");
        RequireUnique(s.Markets.Select(m => m.Code), code, "Market");
        ValidateMarket(market);
        s.Markets.Add(market with { Code = code });
    }, ct);

    public Task UpdateMarketAsync(string code, MarketParameters market, CancellationToken ct = default) => MutateAsync(s =>
    {
        var index = RequireIndex(s.Markets.FindIndex(m => m.Code == code), "Market", code);
        ValidateMarket(market);
        s.Markets[index] = market with { Code = code };
    }, ct);

    public Task DeleteMarketAsync(string code, CancellationToken ct = default) => MutateAsync(s =>
    {
        var exists = s.Markets.Any(m => m.Code == code);
        if (exists && s.Markets.Count == 1)
        {
            throw new InvalidOperationException("Cannot delete the last market — at least one market is required to price.");
        }
        s.Markets.RemoveAll(m => m.Code == code);
    }, ct);

    // --- shared plumbing ---
    private async Task MutateAsync(Action<CatalogueSnapshot> mutate, CancellationToken ct)
    {
        var snapshot = await store.LoadAsync(ct);
        mutate(snapshot);
        await store.SaveMastersAsync(snapshot, ct);
    }

    private static void RequireNonEmpty(string identity, string label)
    {
        if (string.IsNullOrWhiteSpace(identity)) { throw new InvalidOperationException($"{label} is required."); }
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

    // Trims + validates the colour rows of a fabric group in place: each colour code non-empty and
    // unique within the group; purchase/shipping costs non-negative.
    private static void NormalizeAndValidateColors(List<FabricColor> colors)
    {
        var seen = new HashSet<string>();
        foreach (var color in colors)
        {
            var colorCode = color.Code.Trim();
            RequireNonEmpty(colorCode, "Fabric color code");
            if (!seen.Add(colorCode)) { throw new InvalidOperationException($"Fabric color '{colorCode}' is listed more than once."); }
            RequireNonNegative(color.PurchasePrice, "Purchase price");
            RequireNonNegative(color.ShippingCost, "Shipping cost");
            color.Code = colorCode;
        }
    }

    // A combination rule needs >= 1 selection (an empty list would match every configuration), no
    // duplicate option within the rule, and non-empty codes. Adjustment may be negative (combo
    // discount) — intentionally not range-checked.
    private static void ValidateCombinationRule(CombinationPriceRule rule)
    {
        if (rule.RequiredSelections.Count == 0) { throw new InvalidOperationException("A combination price rule needs at least one required selection."); }
        var seen = new HashSet<string>();
        foreach (var key in rule.RequiredSelections)
        {
            RequireNonEmpty(key.OptionDefinitionCode, "Option code");
            RequireNonEmpty(key.ChoiceCode, "Choice code");
            if (!seen.Add(key.OptionDefinitionCode)) { throw new InvalidOperationException($"Combination price rule references option '{key.OptionDefinitionCode}' more than once."); }
        }
    }

    private static void ValidateMarket(MarketParameters market)
    {
        RequireNonNegative(market.TransportRatePerUnit, "Transport rate per unit");
        RequireNonNegative(market.FixedCostPercent, "Fixed cost percent");
        foreach (var step in market.MarkupSteps)
        {
            RequireNonEmpty(step.Name, "Markup step name");
            RequireNonNegative(step.Percent, "Markup percent");
        }
        RequireNonNegative(market.Rounding.LineDecimals, "Line decimals");
        RequireNonNegative(market.Rounding.FinalDecimals, "Final decimals");
        if (market.Rounding.LineDecimals > 28) { throw new InvalidOperationException("Line decimals cannot exceed 28."); }
        if (market.Rounding.FinalDecimals > 28) { throw new InvalidOperationException("Final decimals cannot exceed 28."); }
    }
}
