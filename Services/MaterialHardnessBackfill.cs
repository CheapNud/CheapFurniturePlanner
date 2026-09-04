using CheapFurniturePlanner.Data;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Task 4b, mirrors DiscountService.BackfillIdentityKeysAsync (see Program.cs, called right after
// Database.Migrate()): before this fix HardnessCode stayed null for every non-Foam (and
// hardness-less Foam) material, so SQLite/Postgres both treated every null row as distinct under the
// (Kind, Code, HardnessCode[, SupplierId]) unique indexes on MaterialStock/MaterialProfile/
// MaterialSupplierTerm (FurniturePlannerContext's comment on those indexes) - two writers racing for
// the same material identity could both insert, silently splitting one balance/profile/term across
// two rows. One-time startup rewrite: every null HardnessCode becomes the "" sentinel the row-layer
// normalization (MaterialNeedsService/MaterialOrderService/ProductionUnitService/MaterialPlanningService)
// always writes going forward. A collision during the rewrite - a pre-existing "" row and a null row
// for the same identity, or two null rows - is a real split this bug already caused: for MaterialStock
// the balances are additive and get summed onto one surviving row (movements are untouched, so the
// audit trail stays intact even though it now points at a merged balance); for MaterialProfile/
// MaterialSupplierTerm there is no quantity to combine, so the newest row (highest id, presumably the
// most recently edited) wins and the older duplicate is discarded. Idempotent no-op once every row
// carries "".
public static class MaterialHardnessBackfill
{
    public static async Task RunAsync(FurniturePlannerContext db, CancellationToken ct = default)
    {
        await BackfillStocksAsync(db, ct);
        await BackfillProfilesAsync(db, ct);
        await BackfillTermsAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task BackfillStocksAsync(FurniturePlannerContext db, CancellationToken ct)
    {
        var nullRows = await db.MaterialStocks.Where(s => s.HardnessCode == null).ToListAsync(ct);
        if (nullRows.Count == 0) { return; }
        foreach (var group in nullRows.GroupBy(s => (s.Kind, s.Code)))
        {
            var survivor = await db.MaterialStocks.FirstOrDefaultAsync(
                s => s.Kind == group.Key.Kind && s.Code == group.Key.Code && s.HardnessCode == "", ct);
            var duplicates = group.ToList();
            if (survivor is null)
            {
                survivor = duplicates[0];
                survivor.HardnessCode = "";
                duplicates.RemoveAt(0);
            }
            foreach (var duplicate in duplicates)
            {
                survivor.Amount += duplicate.Amount;
                db.MaterialStocks.Remove(duplicate);
            }
        }
    }

    private static async Task BackfillProfilesAsync(FurniturePlannerContext db, CancellationToken ct)
    {
        var nullRows = await db.MaterialProfiles.Where(p => p.HardnessCode == null).ToListAsync(ct);
        if (nullRows.Count == 0) { return; }
        foreach (var group in nullRows.GroupBy(p => (p.Kind, p.Code)))
        {
            var survivor = await db.MaterialProfiles.FirstOrDefaultAsync(
                p => p.Kind == group.Key.Kind && p.Code == group.Key.Code && p.HardnessCode == "", ct);
            var candidates = group.ToList();
            if (survivor is not null) { candidates.Add(survivor); }
            var winner = candidates.OrderByDescending(p => p.Id).First(); // newest wins - no quantity to sum
            winner.HardnessCode = "";
            foreach (var loser in candidates.Where(p => p.Id != winner.Id)) { db.MaterialProfiles.Remove(loser); }
        }
    }

    private static async Task BackfillTermsAsync(FurniturePlannerContext db, CancellationToken ct)
    {
        var nullRows = await db.MaterialSupplierTerms.Where(t => t.HardnessCode == null).ToListAsync(ct);
        if (nullRows.Count == 0) { return; }
        foreach (var group in nullRows.GroupBy(t => (t.Kind, t.Code, t.SupplierId)))
        {
            var survivor = await db.MaterialSupplierTerms.FirstOrDefaultAsync(
                t => t.Kind == group.Key.Kind && t.Code == group.Key.Code && t.SupplierId == group.Key.SupplierId && t.HardnessCode == "", ct);
            var candidates = group.ToList();
            if (survivor is not null) { candidates.Add(survivor); }
            var winner = candidates.OrderByDescending(t => t.Id).First(); // newest wins - no quantity to sum
            winner.HardnessCode = "";
            // A merge must never silently drop the one preferred-supplier flag on this material identity.
            if (candidates.Any(t => t.IsPreferred)) { winner.IsPreferred = true; }
            foreach (var loser in candidates.Where(t => t.Id != winner.Id)) { db.MaterialSupplierTerms.Remove(loser); }
        }
    }
}
