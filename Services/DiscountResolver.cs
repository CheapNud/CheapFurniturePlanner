using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;

namespace CheapFurniturePlanner.Services;

public sealed record DiscountSuggestion(decimal? RatePercent, decimal? FixedPrice, DiscountScope Scope);

// The guessing function: walks the seller's rule list most-specific-first and returns the first
// match. Within a tier a collection-specific rule beats a wildcard (CollectionCode == null) one.
// Pure — the caller supplies the rules and the line's resolved context; storage never leaks in.
public static class DiscountResolver
{
    public static DiscountSuggestion? Suggest(
        IReadOnlyList<DiscountRule> sellerRules,
        string? collectionCode,
        string modelCode,
        ModelType? modelType,
        string elementCode,
        string? priceGroupCode,
        string? materialTypeCode)
    {
        return Match(DiscountScope.ElementPriceGroup, r => priceGroupCode is not null && r.ElementCode == elementCode && r.PriceGroupCode == priceGroupCode)
            ?? Match(DiscountScope.Model, r => r.ModelCode == modelCode)
            ?? Match(DiscountScope.ModelType, r => modelType is not null && r.ModelType == modelType)
            ?? Match(DiscountScope.MaterialType, r => materialTypeCode is not null && r.MaterialTypeCode == materialTypeCode)
            ?? Match(DiscountScope.Everything, _ => true);

        DiscountSuggestion? Match(DiscountScope scope, Func<DiscountRule, bool> keysMatch)
        {
            var candidates = sellerRules.Where(r => r.Scope == scope && keysMatch(r));
            var winner = candidates.FirstOrDefault(r => r.CollectionCode is not null && r.CollectionCode == collectionCode)
                ?? candidates.FirstOrDefault(r => r.CollectionCode is null);
            return winner is null ? null : new DiscountSuggestion(winner.RatePercent, winner.FixedPrice, scope);
        }
    }
}
