using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Shared retry for MaterialOrderService.ReceiveAsync, ProductionUnitService.ApplyBackflushAsync
// (via SaveOrThrowFriendlyConflictAsync) and MaterialNeedsService.AdjustStockAsync: all three
// find-or-create a MaterialStock row by (Kind, Code, HardnessCode) and can lose a race between that
// read and their own insert once a second writer's insert lands first, tripping the unique index
// (FurniturePlannerContext.OnModelCreating). On that DbUpdateException the failed Added row is
// swapped for the now-real one and its intended change re-applied: additive for a delta write
// (Receive/backflush start the new row at 0, so the failed insert's own Amount IS the delta already),
// absolute for a set write (AdjustStockAsync's "this is what's actually on the shelf"). Everything
// else already pending on `db` (movement rows, a unit's own Version bump) rolled back with the first
// attempt and re-saves untouched on the retry, so nothing lands twice.
internal static class MaterialStockUpsertRetry
{
    public static async Task SaveAsync(FurniturePlannerContext db, bool additive, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return; }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            var collided = false;
            foreach (var entry in ex.Entries.Where(e => e.State == EntityState.Added && e.Entity is MaterialStock).ToList())
            {
                var failedInsert = (MaterialStock)entry.Entity;
                var existing = await db.MaterialStocks.AsNoTracking().FirstOrDefaultAsync(
                    s => s.Kind == failedInsert.Kind && s.Code == failedInsert.Code && s.HardnessCode == failedInsert.HardnessCode, ct);
                if (existing is null) { continue; } // not actually a stock collision - falls through to rethrow below
                entry.State = EntityState.Detached;
                db.Attach(existing);

                // Task 4b: an absolute-set caller (AdjustStockAsync) staged its own MaterialMovement
                // assuming its losing find saw no row at all (oldAmount = 0), so that movement's
                // Quantity currently equals failedInsert.Amount (the intended new balance) in full.
                // Now that the retry knows the real prior amount, correct the movement to the true
                // delta instead of overstating it by whatever the racer already had on the shelf. An
                // additive caller (Receive/backflush) never needs this - its movement Quantity is
                // already a fixed delta, correct regardless of whether the row pre-existed.
                if (!additive)
                {
                    // failedInsert.HardnessCode is the row-layer "" sentinel (Task 4b); the staged
                    // movement keeps whatever raw value the caller passed in (null for non-Foam) - both
                    // sides normalized here so the match still finds it.
                    var movement = db.ChangeTracker.Entries<MaterialMovement>().FirstOrDefault(m =>
                        m.State == EntityState.Added && m.Entity.Kind == failedInsert.Kind && m.Entity.Code == failedInsert.Code
                        && (m.Entity.HardnessCode ?? "") == (failedInsert.HardnessCode ?? ""));
                    if (movement is not null) { movement.Entity.Quantity -= existing.Amount; }
                }

                existing.Amount = additive ? existing.Amount + failedInsert.Amount : failedInsert.Amount;
                existing.UpdatedAt = failedInsert.UpdatedAt;
                collided = true;
            }
            if (!collided) { throw; }
        }
        await db.SaveChangesAsync(ct);
    }
}
