using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
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
        rule.IdentityKey = ComputeIdentityKey(rule);
        await using var db = await factory.CreateDbContextAsync(ct);
        var duplicate = await db.DiscountRules.AnyAsync(r => r.SellerId == rule.SellerId && r.IdentityKey == rule.IdentityKey, ct);
        if (duplicate) { throw new InvalidOperationException("An identical discount rule already exists for this seller."); }
        db.DiscountRules.Add(rule);
        await db.SaveChangesAsync(ct);
    }

    // One-time startup pass (see Program.cs, run right after Database.Migrate()): pre-MB1 rows carry
    // the IdentityKey default (""), which the backstop index below deliberately excludes so the
    // migration itself can never fail on them. This computes real keys for those rows before
    // anything else can write a new rule. A genuine identity collision among pre-existing rows would
    // mean the app guard's own TOCTOU race actually fired at some point in this database's history
    // (see the index comment in FurniturePlannerContext) - vanishingly rare, but instead of silently
    // dropping a row this appends its id to the key so the row survives and the collision stays
    // visible to anyone who looks at the data.
    public async Task BackfillIdentityKeysAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var unbackfilled = await db.DiscountRules.Where(r => r.IdentityKey == "").ToListAsync(ct);
        if (unbackfilled.Count == 0) { return; }
        var seen = new HashSet<string>(await db.DiscountRules.Where(r => r.IdentityKey != "").Select(r => r.IdentityKey).ToListAsync(ct));
        foreach (var rule in unbackfilled)
        {
            var key = ComputeIdentityKey(rule);
            if (!seen.Add(key)) { key = $"{key}{KeySeparator}{rule.Id}"; seen.Add(key); }
            rule.IdentityKey = key;
        }
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

    // U+0001 (a control character) joins the fields: no code entered through the admin UI's text
    // fields can type one, so it can never collide with real code content the way a printable
    // separator like '|' or '-' could. "~" marks a null scope column - the fixed sentinel that makes
    // two rules with the same null columns compare equal instead of NULL <> NULL.
    private const string KeySeparator = "\u0001";
    private const string NullToken = "~";

    private static string ComputeIdentityKey(DiscountRule rule) => string.Join(KeySeparator,
        rule.SellerId.ToString(),
        rule.CollectionCode ?? NullToken,
        rule.Scope.ToString(),
        rule.ElementCode ?? NullToken,
        rule.PriceGroupCode ?? NullToken,
        rule.ModelCode ?? NullToken,
        rule.ModelType?.ToString() ?? NullToken,
        rule.MaterialTypeCode ?? NullToken);

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
        RequireModelType(needsModelType, rule.ModelType);
        Require(needsMaterialType, rule.MaterialTypeCode, "material type code");

        static void Require(bool needed, string? key, string label)
        {
            if (needed && string.IsNullOrWhiteSpace(key)) { throw new InvalidOperationException($"This scope requires a {label}."); }
            if (!needed && key is not null) { throw new InvalidOperationException($"This scope must not carry a {label}."); }
        }

        static void RequireModelType(bool needed, ModelType? modelType)
        {
            if (needed && modelType is null) { throw new InvalidOperationException("This scope requires a model type."); }
            if (!needed && modelType is not null) { throw new InvalidOperationException("This scope must not carry a model type."); }
        }
    }
}
