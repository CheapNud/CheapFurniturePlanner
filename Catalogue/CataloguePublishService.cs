using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Catalogue;

public record PublishResult(bool Success, IReadOnlyList<string> Errors, string? Version);

public sealed class CataloguePublishService(IDbContextFactory<FurniturePlannerContext> factory, ICatalogueSource source)
{
    public async Task<PublishResult> PublishAsync(CatalogueSnapshot snapshot)
    {
        List<string> errors = Validate(snapshot);
        if (errors.Count > 0)
        {
            return new PublishResult(false, errors, null);
        }

        await using var ctx = await factory.CreateDbContextAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var existingVersions = await ctx.PublishedCatalogues
            .Select(c => c.Version).ToListAsync();
        var next = (existingVersions.Select(v => int.TryParse(v, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1).ToString();

        foreach (var current in await ctx.PublishedCatalogues.Where(c => c.IsCurrent).ToListAsync())
        {
            current.IsCurrent = false;
        }

        snapshot.Version = next;
        var hash = snapshot.ComputeContentHash();
        snapshot.ContentHash = hash;

        ctx.PublishedCatalogues.Add(new PublishedCatalogue
        {
            Version = next,
            ContentHash = hash,
            BundleJson = CanonicalJson.Serialize(snapshot),
            IsCurrent = true,
            PublishedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        await tx.CommitAsync();
        source.Invalidate();
        return new PublishResult(true, [], next);
    }

    private static List<string> Validate(CatalogueSnapshot snapshot)
    {
        List<string> errors = [];
        var priceGroupCodes = snapshot.PriceGroups.Select(p => p.Code).ToHashSet();
        var operationCodes = snapshot.Operations.Select(o => o.Code).ToHashSet();
        var frameCodes = snapshot.FrameBodies.Select(f => f.Code).ToHashSet();
        var materialCodes = snapshot.Materials.Select(m => m.Code).ToHashSet();
        var fabricGroupCodes = snapshot.FabricGroups.Select(g => g.Code).ToHashSet();

        foreach (var group in snapshot.FabricGroups)
        {
            if (!priceGroupCodes.Contains(group.PriceGroupCode))
            {
                errors.Add($"FabricGroup '{group.Code}' references missing PriceGroup '{group.PriceGroupCode}'.");
            }
        }
        foreach (var model in snapshot.Models)
        {
            if (model.Elements.Count == 0)
            {
                errors.Add($"Model '{model.Code}' has no elements and cannot be published.");
            }
            foreach (var element in model.Elements)
            {
                foreach (var option in element.Options)
                {
                    if (option.OptionDefinitionCode == VariantCode.MaterialDefCode)
                    {
                        errors.Add($"Element '{element.Code}' uses reserved option code '{VariantCode.MaterialDefCode}'.");
                    }
                    if (option is FabricOption fabricOption)
                    {
                        foreach (var fabricGroupCode in fabricOption.FabricGroupCodes)
                        {
                            if (!fabricGroupCodes.Contains(fabricGroupCode))
                            {
                                errors.Add($"Element '{element.Code}' BOM references missing fabric group '{fabricGroupCode}'.");
                            }
                        }
                    }
                    foreach (var rule in option.VisibilityRules)
                    {
                        var trigger = element.Options.FirstOrDefault(o => o.OptionDefinitionCode == rule.TriggerOptionDefinitionCode);
                        var triggerHasChoice = trigger is ChoiceOption triggerChoice
                            && triggerChoice.Values.Any(v => v.OptionChoiceCode == rule.TriggerChoiceCode);
                        if (!triggerHasChoice)
                        {
                            errors.Add($"Element '{element.Code}' option '{option.OptionDefinitionCode}' has a visibility rule referencing unknown trigger '{rule.TriggerOptionDefinitionCode}:{rule.TriggerChoiceCode}'.");
                        }
                    }
                }
                foreach (var section in element.Bom.Sections)
                {
                    foreach (var line in section.Lines)
                    {
                        errors.AddRange(MissingBomCodes(element.Code, line, operationCodes, frameCodes, materialCodes, priceGroupCodes));
                        if (line.Condition is not null)
                        {
                            foreach (var key in line.Condition.RequiredSelections)
                            {
                                // The synthetic __MATERIAL__ selection is never an authored ChoiceOption; it is
                                // injected from the resolved material type at pricing time, so it always resolves.
                                var conditionOption = element.Options.FirstOrDefault(o => o.OptionDefinitionCode == key.OptionDefinitionCode);
                                var resolves = key.OptionDefinitionCode == VariantCode.MaterialDefCode
                                    || (conditionOption is ChoiceOption conditionChoice
                                        && conditionChoice.Values.Any(v => v.OptionChoiceCode == key.ChoiceCode));
                                if (!resolves)
                                {
                                    errors.Add($"Element '{element.Code}' BOM line '{line.LineKey}' has a condition referencing unknown selection '{key.OptionDefinitionCode}:{key.ChoiceCode}'.");
                                }
                            }
                        }
                    }
                }
                foreach (var rule in element.Substitutions)
                {
                    if (!materialCodes.Contains(rule.WithMaterialCode))
                    {
                        errors.Add($"Element '{element.Code}' substitution references missing material '{rule.WithMaterialCode}'.");
                    }
                }
            }
        }
        return errors;
    }

    private static IEnumerable<string> MissingBomCodes(string elementCode, BomLine line,
        HashSet<string> ops, HashSet<string> frames, HashSet<string> materials, HashSet<string> priceGroups)
    {
        switch (line)
        {
            case LaborBomLine labor when !ops.Contains(labor.OperationCode):
                yield return $"Element '{elementCode}' BOM references missing operation '{labor.OperationCode}'.";
                break;
            case FrameBomLine frame when !frames.Contains(frame.FrameBodyCode):
                yield return $"Element '{elementCode}' BOM references missing frame body '{frame.FrameBodyCode}'.";
                break;
            case FoamBomLine foam when !materials.Contains(foam.FoamCode):
                yield return $"Element '{elementCode}' BOM references missing material '{foam.FoamCode}'.";
                break;
            case CottonBomLine cotton when !materials.Contains(cotton.CottonQualityCode):
                yield return $"Element '{elementCode}' BOM references missing material '{cotton.CottonQualityCode}'.";
                break;
            case CutSortBomLine cutSort:
                foreach (var groupCode in cutSort.SecondaryGroupMetrages.Keys)
                {
                    if (!priceGroups.Contains(groupCode))
                    {
                        yield return $"Element '{elementCode}' BOM references missing price group '{groupCode}'.";
                    }
                }
                break;
            case MiscBomLine misc when !materials.Contains(misc.MaterialCode):
                yield return $"Element '{elementCode}' BOM references missing material '{misc.MaterialCode}'.";
                break;
        }
    }
}
