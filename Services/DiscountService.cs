using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// CRUD over a seller's discount rule list. Update edits the values only; scope and keys are
// immutable (delete and re-add to re-key) — keeps rule identity stable and the admin UI simple.
public sealed class DiscountService(IDbContextFactory<FurniturePlannerContext> factory)
{
    public async Task<List<DiscountRule>> RulesForSellerAsync(int sellerId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.DiscountRules.AsNoTracking().Where(r => r.SellerId == sellerId)
            .OrderBy(r => r.Scope).ThenBy(r => r.CollectionCode).ToListAsync(ct);
    }

    public async Task AddRuleAsync(DiscountRule rule, CancellationToken ct = default)
    {
        Validate(rule);
        await using var db = await factory.CreateDbContextAsync(ct);
        var duplicate = await db.DiscountRules.AnyAsync(r => r.SellerId == rule.SellerId
            && r.CollectionCode == rule.CollectionCode && r.Scope == rule.Scope
            && r.ElementCode == rule.ElementCode && r.PriceGroupCode == rule.PriceGroupCode
            && r.ModelCode == rule.ModelCode && r.ModelTypeCode == rule.ModelTypeCode
            && r.MaterialTypeCode == rule.MaterialTypeCode, ct);
        if (duplicate) { throw new InvalidOperationException("An identical discount rule already exists for this seller."); }
        db.DiscountRules.Add(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateRuleAsync(int id, decimal? ratePercent, decimal? fixedPrice, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rule = await db.DiscountRules.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new InvalidOperationException($"Discount rule {id} not found.");
        rule.RatePercent = ratePercent;
        rule.FixedPrice = fixedPrice;
        Validate(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteRuleAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.DiscountRules.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
    }

    private static void Validate(DiscountRule rule)
    {
        if (rule.RatePercent is null == rule.FixedPrice is null)
        {
            throw new InvalidOperationException("A discount rule needs exactly one of rate percent or fixed price.");
        }
        if (rule.RatePercent is < 0 or > 100) { throw new InvalidOperationException("Rate percent must be between 0 and 100."); }
        if (rule.FixedPrice is < 0) { throw new InvalidOperationException("Fixed price cannot be negative."); }
        if (rule.FixedPrice is not null && rule.Scope != DiscountScope.ElementPriceGroup)
        {
            throw new InvalidOperationException("A fixed price is only valid on an element + price group rule.");
        }
        var (needsElement, needsModel, needsModelType, needsMaterialType) = rule.Scope switch
        {
            DiscountScope.ElementPriceGroup => (true, false, false, false),
            DiscountScope.Model => (false, true, false, false),
            DiscountScope.ModelType => (false, false, true, false),
            DiscountScope.MaterialType => (false, false, false, true),
            _ => (false, false, false, false),
        };
        Require(needsElement, rule.ElementCode, "element code");
        Require(needsElement, rule.PriceGroupCode, "price group code");
        Require(needsModel, rule.ModelCode, "model code");
        Require(needsModelType, rule.ModelTypeCode, "model type code");
        Require(needsMaterialType, rule.MaterialTypeCode, "material type code");

        static void Require(bool needed, string? key, string label)
        {
            if (needed && string.IsNullOrWhiteSpace(key)) { throw new InvalidOperationException($"This scope requires a {label}."); }
            if (!needed && key is not null) { throw new InvalidOperationException($"This scope must not carry a {label}."); }
        }
    }
}
