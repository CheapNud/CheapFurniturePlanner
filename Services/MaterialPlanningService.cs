using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// MRP knobs editor: MaterialProfile (demand-side minimum stock / usage override) and
// MaterialSupplierTerm (supply-side lead time / MOQ / price per supplier) - both keyed by the
// same material identity (Kind, Code, HardnessCode) MaterialStock already uses. A later task reads
// these to turn MaterialNeedsService's forecast into reorder suggestions; this task is just the
// CRUD + the one real invariant: exactly one preferred term per material identity (the
// FirmService default-invariant idiom - first-auto, atomic clear-then-set swap, delete-guard).
public sealed class MaterialPlanningService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser)
{
    // --- Profiles ---

    public async Task<List<MaterialProfile>> ProfilesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MaterialProfiles.AsNoTracking()
            .OrderBy(p => p.Kind).ThenBy(p => p.Code).ToListAsync(ct);
    }

    // Upsert by identity (Kind, Code, HardnessCode) - a profile can be authored before any stock
    // or terms row exists for the same identity, so there is no separate Add/Update split here.
    public async Task<MaterialProfile> UpsertProfileAsync(MaterialProfile profileValues, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var code = RequireTrimmed(profileValues.Code, "Material code");
        if (profileValues.MinimumStock < 0) { throw new InvalidOperationException("Minimum stock cannot be negative."); }
        if (profileValues.AverageUsageOverride is <= 0) { throw new InvalidOperationException("Average usage override must be positive when set."); }
        var hardnessCode = NormalizeHardness(profileValues.HardnessCode);

        await using var db = await factory.CreateDbContextAsync(ct);
        var profile = await db.MaterialProfiles.FirstOrDefaultAsync(
            p => p.Kind == profileValues.Kind && p.Code == code && p.HardnessCode == hardnessCode, ct);
        if (profile is null)
        {
            profile = new MaterialProfile { Kind = profileValues.Kind, Code = code, HardnessCode = hardnessCode };
            db.MaterialProfiles.Add(profile);
        }
        profile.MinimumStock = profileValues.MinimumStock;
        profile.AverageUsageOverride = profileValues.AverageUsageOverride;
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task DeleteProfileAsync(int id, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.MaterialProfiles.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
    }

    // --- Supplier terms ---

    public async Task<List<MaterialSupplierTerm>> TermsAsync(MaterialKind kind, string code, string? hardnessCode, CancellationToken ct = default)
    {
        var normalizedHardness = NormalizeHardness(hardnessCode);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MaterialSupplierTerms.AsNoTracking()
            .Include(t => t.Supplier)
            .Where(t => t.Kind == kind && t.Code == code && t.HardnessCode == normalizedHardness)
            .OrderByDescending(t => t.IsPreferred).ThenBy(t => t.SupplierId).ToListAsync(ct);
    }

    public async Task<List<MaterialSupplierTerm>> AllTermsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MaterialSupplierTerms.AsNoTracking()
            .Include(t => t.Supplier)
            .OrderBy(t => t.Kind).ThenBy(t => t.Code).ThenBy(t => t.SupplierId).ToListAsync(ct);
    }

    // Upsert by identity (Kind, Code, HardnessCode, SupplierId) - unique per material+supplier.
    // The very first term recorded for a material identity is auto-preferred (nothing else to pick
    // from); every later one joins as a non-preferred alternative until SetPreferredAsync swaps it
    // in. IsPreferred is deliberately not part of the upsert payload - only SetPreferredAsync moves it.
    public async Task<MaterialSupplierTerm> UpsertTermAsync(MaterialSupplierTerm termValues, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var code = RequireTrimmed(termValues.Code, "Material code");
        if (termValues.DeliveryTimeDays < 0) { throw new InvalidOperationException("Delivery time cannot be negative."); }
        if (termValues.MinimumOrderQuantity is <= 0) { throw new InvalidOperationException("Minimum order quantity must be positive when set."); }
        if (termValues.UnitsPerPackage is <= 0) { throw new InvalidOperationException("Units per package must be positive when set."); }
        if (termValues.UnitPrice is <= 0) { throw new InvalidOperationException("Unit price must be positive when set."); }
        var hardnessCode = NormalizeHardness(termValues.HardnessCode);

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Suppliers.AnyAsync(s => s.Id == termValues.SupplierId, ct))
        {
            throw new InvalidOperationException($"Supplier {termValues.SupplierId} not found.");
        }
        var term = await db.MaterialSupplierTerms.FirstOrDefaultAsync(
            t => t.Kind == termValues.Kind && t.Code == code && t.HardnessCode == hardnessCode && t.SupplierId == termValues.SupplierId, ct);
        if (term is null)
        {
            var isFirstForMaterial = !await db.MaterialSupplierTerms.AnyAsync(
                t => t.Kind == termValues.Kind && t.Code == code && t.HardnessCode == hardnessCode, ct);
            term = new MaterialSupplierTerm
            {
                Kind = termValues.Kind,
                Code = code,
                HardnessCode = hardnessCode,
                SupplierId = termValues.SupplierId,
                IsPreferred = isFirstForMaterial,
            };
            db.MaterialSupplierTerms.Add(term);
        }
        term.DeliveryTimeDays = termValues.DeliveryTimeDays;
        term.MinimumOrderQuantity = termValues.MinimumOrderQuantity;
        term.UnitsPerPackage = termValues.UnitsPerPackage;
        term.UnitPrice = termValues.UnitPrice;
        await db.SaveChangesAsync(ct);
        return term;
    }

    // Atomic swap within the material identity (FirmService.SetDefaultAsync idiom) - no filtered
    // unique index backs IsPreferred (unlike ConsumerDeliveryAddress's default flag), so a plain
    // clear-then-set in one SaveChanges is enough, no transaction dance needed.
    public async Task SetPreferredAsync(int termId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var term = await db.MaterialSupplierTerms.FirstOrDefaultAsync(t => t.Id == termId, ct)
            ?? throw new InvalidOperationException($"Term {termId} not found.");
        var siblings = await db.MaterialSupplierTerms
            .Where(t => t.Kind == term.Kind && t.Code == term.Code && t.HardnessCode == term.HardnessCode && t.Id != term.Id && t.IsPreferred)
            .ToListAsync(ct);
        foreach (var sibling in siblings) { sibling.IsPreferred = false; }
        term.IsPreferred = true;
        await db.SaveChangesAsync(ct);
    }

    // Deleting the preferred term while siblings remain would leave the material identity with no
    // preferred term at all - guarded the same way FirmService.DeleteFirmAsync guards the default
    // firm. Deleting the last remaining term (necessarily the preferred one, nothing else to be
    // preferred over) is allowed - the identity simply has no terms left.
    public async Task DeleteTermAsync(int id, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var term = await db.MaterialSupplierTerms.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new InvalidOperationException($"Term {id} not found.");
        if (term.IsPreferred && await db.MaterialSupplierTerms.AnyAsync(
                t => t.Kind == term.Kind && t.Code == term.Code && t.HardnessCode == term.HardnessCode && t.Id != term.Id, ct))
        {
            throw new InvalidOperationException("Make another term preferred first.");
        }
        db.MaterialSupplierTerms.Remove(term);
        await db.SaveChangesAsync(ct);
    }

    // --- Movements ---

    // Read-only, like MaterialNeedsService.StockAsync - /materials is already Admin-or-Office gated,
    // no separate role check needed. Newest first, capped (the Movements dialog's "recent history"
    // view, not a full ledger export) - Id as a tie-break keeps ordering stable across same-instant rows.
    public async Task<List<MaterialMovement>> MovementsAsync(MaterialKind kind, string code, string? hardnessCode, int take = 50, CancellationToken ct = default)
    {
        var normalizedHardness = NormalizeHardness(hardnessCode);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MaterialMovements.AsNoTracking()
            .Where(m => m.Kind == kind && m.Code == code && m.HardnessCode == normalizedHardness)
            .OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.Id)
            .Take(take).ToListAsync(ct);
    }

    private static string? NormalizeHardness(string? hardnessCode) =>
        string.IsNullOrWhiteSpace(hardnessCode) ? null : hardnessCode.Trim();

    private static string RequireTrimmed(string value, string fieldLabel)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0) { throw new InvalidOperationException($"{fieldLabel} is required."); }
        return trimmed;
    }

    private async Task RequireAdminOrOfficeAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office)) { return; }
        throw new InvalidOperationException("Only Admin or Office can do this.");
    }
}
