using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// UnresolvedModelCodes carries unresolved *identities*, not strictly model codes: a
// StandaloneArticle line has no ModelCode by construction, so an unresolvable standalone unit is
// identified by its article's AssignedCode (or, failing that, the unit's own UnitCode) instead.
public sealed record SweepResult(List<int> SupplierOrderIds, List<string> UnresolvedModelCodes);

// Sweeps placed, warehouse-bound production units into supplier purchase orders. A unit's
// supplier is resolved from its order line (dropship SupplierId) first, falling back to the
// model-code map; unresolved units are left alone and surface via UnresolvedModelCodesAsync for
// the warning panel. Draft POs are the only mutable state - Sent/Completed are frozen except for
// TheirReference. Numbering copies ProductionUnitService.CreateTripAsync's max-suffix pattern
// (not count-based: Drafts are deletable, so a count would collide with a still-live PO).
public sealed class PurchasingService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser)
{
    public async Task<SweepResult> GenerateOrdersAsync(CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var (bySupplier, unresolvedModelCodes) = await ResolveCandidatesAsync(db, ct);

        var prefix = $"PO-{DateTime.UtcNow.Year}-";
        var codesThisYear = await db.SupplierOrders.Where(o => o.PoNumber.StartsWith(prefix)).Select(o => o.PoNumber).ToListAsync(ct);
        var maxSuffix = 0;
        foreach (var code in codesThisYear)
        {
            if (int.TryParse(code[prefix.Length..], out var suffix) && suffix > maxSuffix) { maxSuffix = suffix; }
        }

        var userId = await RequireUserIdAsync();
        var touchedOrders = new List<SupplierOrder>();
        foreach (var (supplierId, units) in bySupplier)
        {
            var draft = await db.SupplierOrders.FirstOrDefaultAsync(o => o.SupplierId == supplierId && o.State == SupplierOrderState.Draft, ct);
            if (draft is null)
            {
                maxSuffix++;
                draft = new SupplierOrder
                {
                    PoNumber = $"{prefix}{maxSuffix:D4}",
                    SupplierId = supplierId,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow,
                };
                db.SupplierOrders.Add(draft);
            }
            foreach (var unit in units) { unit.SupplierOrder = draft; }
            touchedOrders.Add(draft);
        }
        await db.SaveChangesAsync(ct);

        return new SweepResult(touchedOrders.Select(o => o.Id).ToList(), unresolvedModelCodes);
    }

    public async Task<List<SupplierOrder>> ListOrdersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SupplierOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Units)
            .OrderByDescending(o => o.PoNumber).ToListAsync(ct);
    }

    public async Task<SupplierOrder?> GetOrderAsync(int supplierOrderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SupplierOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Units).ThenInclude(u => u.Order)!.ThenInclude(o => o!.Consumer)
            .Include(o => o.Units).ThenInclude(u => u.Order)!.ThenInclude(o => o!.Lines)
            .FirstOrDefaultAsync(o => o.Id == supplierOrderId, ct);
    }

    public async Task ReleaseUnitAsync(int unitId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var unit = await db.ProductionUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct)
            ?? throw new InvalidOperationException($"Unit {unitId} not found.");
        if (unit.SupplierOrderId is not int supplierOrderId) { throw new InvalidOperationException($"Unit {unit.UnitCode} is not on a purchase order."); }
        var order = await db.SupplierOrders.FirstAsync(o => o.Id == supplierOrderId, ct);
        if (order.State != SupplierOrderState.Draft) { throw new InvalidOperationException($"Purchase order {order.PoNumber} is no longer a draft."); }
        unit.SupplierOrderId = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteOrderAsync(int supplierOrderId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.SupplierOrders.Include(o => o.Units).FirstOrDefaultAsync(o => o.Id == supplierOrderId, ct)
            ?? throw new InvalidOperationException($"Purchase order {supplierOrderId} not found.");
        if (order.State != SupplierOrderState.Draft || order.Units.Count > 0)
        {
            throw new InvalidOperationException($"Purchase order {order.PoNumber} is not an empty draft.");
        }
        db.SupplierOrders.Remove(order);
        await db.SaveChangesAsync(ct);
    }

    public async Task SendAsync(int supplierOrderId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.SupplierOrders.Include(o => o.Units).FirstOrDefaultAsync(o => o.Id == supplierOrderId, ct)
            ?? throw new InvalidOperationException($"Purchase order {supplierOrderId} not found.");
        if (order.State != SupplierOrderState.Draft) { throw new InvalidOperationException($"Purchase order {order.PoNumber} is not a draft."); }
        if (order.Units.Count == 0) { throw new InvalidOperationException($"Purchase order {order.PoNumber} has no units."); }
        order.State = SupplierOrderState.Sent;
        order.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetTheirReferenceAsync(int supplierOrderId, string? theirReference, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == supplierOrderId, ct)
            ?? throw new InvalidOperationException($"Purchase order {supplierOrderId} not found.");
        // Sent or Completed - the class comment promises TheirReference records the supplier's
        // confirmation ref, and a PO can auto-complete (last unit arrives) before anyone gets
        // around to typing that ref in, so a fast-completing PO must still accept it.
        if (order.State is not (SupplierOrderState.Sent or SupplierOrderState.Completed))
        {
            throw new InvalidOperationException($"Purchase order {order.PoNumber} has not been sent.");
        }
        order.TheirReference = string.IsNullOrWhiteSpace(theirReference) ? null : theirReference.Trim();
        await db.SaveChangesAsync(ct);
    }

    // Same identity rule as SweepResult.UnresolvedModelCodes above: model codes OR article codes
    // (unresolved identities), never a silently-dropped unit.
    public async Task<List<string>> UnresolvedModelCodesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (_, unresolvedModelCodes) = await ResolveCandidatesAsync(db, ct);
        return unresolvedModelCodes;
    }

    // The resolution rule: candidates are Expected, not-yet-ordered units whose line ships to our
    // warehouse; each resolves via its line's own SupplierId (dropship pin) first, else the
    // model-code map. Anything left over is unresolved - reported distinct, sorted for determinism.
    private static async Task<(Dictionary<int, List<ProductionUnit>> BySupplier, List<string> UnresolvedModelCodes)> ResolveCandidatesAsync(
        FurniturePlannerContext db, CancellationToken ct)
    {
        var modelCodeToSupplierId = await db.SupplierModelMaps.AsNoTracking().ToDictionaryAsync(m => m.ModelCode, m => m.SupplierId, ct);

        var candidates = await db.ProductionUnits
            .Where(u => u.State == ProductionUnitState.Expected && u.SupplierOrderId == null)
            .Join(db.OrderLines.Where(l => l.DeliverToWarehouse), u => u.OrderLineId, l => l.Id,
                (u, l) => new { Unit = u, l.SupplierId, l.ModelCode, l.AssignedCode })
            .ToListAsync(ct);

        var bySupplier = new Dictionary<int, List<ProductionUnit>>();
        var unresolvedModelCodes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var supplierId = candidate.SupplierId
                ?? (candidate.ModelCode is not null && modelCodeToSupplierId.TryGetValue(candidate.ModelCode, out var mappedSupplierId) ? mappedSupplierId : null);
            if (supplierId is null)
            {
                // A StandaloneArticle line has no ModelCode - fall back to its AssignedCode, then
                // the unit's own UnitCode, so an unresolvable unit always surfaces an identity
                // instead of vanishing from both the sweep and the warning feed.
                unresolvedModelCodes.Add(candidate.ModelCode ?? candidate.AssignedCode ?? candidate.Unit.UnitCode);
                continue;
            }
            if (!bySupplier.TryGetValue(supplierId.Value, out var units)) { bySupplier[supplierId.Value] = units = []; }
            units.Add(candidate.Unit);
        }
        return (bySupplier, unresolvedModelCodes.ToList());
    }

    public async Task<SupplierDelivery> CreateAnnouncementAsync(int supplierId, string reference, DateTime? expectedDate, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var trimmedReference = (reference ?? "").Trim();
        if (trimmedReference.Length == 0) { throw new InvalidOperationException("A reference is required."); }
        await using var db = await factory.CreateDbContextAsync(ct);
        // Pre-check the (SupplierId, Reference) unique index so a duplicate reads as a friendly
        // message through the UI instead of a raw DbUpdateException.
        if (await db.SupplierDeliveries.AnyAsync(d => d.SupplierId == supplierId && d.Reference == trimmedReference, ct))
        {
            throw new InvalidOperationException($"An announcement with reference '{trimmedReference}' already exists for this supplier.");
        }
        var announcement = new SupplierDelivery
        {
            SupplierId = supplierId,
            Reference = trimmedReference,
            ExpectedDate = expectedDate,
            CreatedByUserId = await RequireUserIdAsync(),
            CreatedAt = DateTime.UtcNow,
        };
        db.SupplierDeliveries.Add(announcement);
        await db.SaveChangesAsync(ct);
        return announcement;
    }

    // open = has a unit that hasn't reached Arrived/Delivered yet, or nothing attached yet (a
    // freshly created announcement still needs to show up so units can be attached to it).
    // Cancelled units count as resolved too - a cancelled unit can never arrive, so it can't be
    // what's holding the announcement open (defense in depth: CancelForOrderAsync also clears the
    // link outright, but this keeps any pre-existing dangling data from reading as open).
    public async Task<List<SupplierDelivery>> ListAnnouncementsAsync(int? supplierId = null, bool openOnly = true, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.SupplierDeliveries.AsNoTracking()
            .Include(d => d.Supplier)
            .Include(d => d.Units)
            .AsQueryable();
        if (supplierId is int wantedSupplierId) { query = query.Where(d => d.SupplierId == wantedSupplierId); }
        if (openOnly)
        {
            query = query.Where(d => d.Units.Count == 0 || d.Units.Any(u =>
                u.State != ProductionUnitState.Arrived && u.State != ProductionUnitState.Delivered && u.State != ProductionUnitState.Cancelled));
        }
        return await query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
    }

    // Guards: the unit must be on a Sent PO (so it's an actual placed order, not a draft
    // candidate), from the same supplier as the announcement, not yet arrived, and not already on
    // another announcement.
    public async Task AttachToAnnouncementAsync(int supplierDeliveryId, int unitId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var announcement = await db.SupplierDeliveries.FirstOrDefaultAsync(d => d.Id == supplierDeliveryId, ct)
            ?? throw new InvalidOperationException($"Announcement {supplierDeliveryId} not found.");
        var unit = await db.ProductionUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct)
            ?? throw new InvalidOperationException($"Unit {unitId} not found.");
        if (unit.SupplierOrderId is not int supplierOrderId) { throw new InvalidOperationException($"Unit {unit.UnitCode} is not on a purchase order."); }
        var order = await db.SupplierOrders.FirstAsync(o => o.Id == supplierOrderId, ct);
        if (order.State != SupplierOrderState.Sent) { throw new InvalidOperationException($"Purchase order {order.PoNumber} has not been sent."); }
        if (order.SupplierId != announcement.SupplierId) { throw new InvalidOperationException($"Unit {unit.UnitCode} belongs to a different supplier."); }
        if (unit.State is ProductionUnitState.Arrived or ProductionUnitState.Delivered) { throw new InvalidOperationException($"Unit {unit.UnitCode} has already arrived."); }
        if (unit.SupplierDeliveryId is not null) { throw new InvalidOperationException($"Unit {unit.UnitCode} is already on an announcement."); }
        unit.SupplierDeliveryId = supplierDeliveryId;
        await db.SaveChangesAsync(ct);
    }

    public async Task DetachFromAnnouncementAsync(int unitId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var unit = await db.ProductionUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct)
            ?? throw new InvalidOperationException($"Unit {unitId} not found.");
        if (unit.SupplierDeliveryId is null) { throw new InvalidOperationException($"Unit {unit.UnitCode} is not on an announcement."); }
        if (unit.State is ProductionUnitState.Arrived or ProductionUnitState.Delivered) { throw new InvalidOperationException($"Unit {unit.UnitCode} has already arrived."); }
        unit.SupplierDeliveryId = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAnnouncementAsync(int supplierDeliveryId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var announcement = await db.SupplierDeliveries.Include(d => d.Units).FirstOrDefaultAsync(d => d.Id == supplierDeliveryId, ct)
            ?? throw new InvalidOperationException($"Announcement {supplierDeliveryId} not found.");
        if (announcement.Units.Count > 0) { throw new InvalidOperationException($"Announcement {announcement.Reference} is not empty."); }
        db.SupplierDeliveries.Remove(announcement);
        await db.SaveChangesAsync(ct);
    }

    // An announcement is overdue once its expected date has passed and at least one attached unit
    // still hasn't reached the dock (Arrived) or beyond (Delivered). Cancelled units can never
    // arrive either, so they don't hold an announcement open/overdue - see ListAnnouncementsAsync.
    public static bool IsOverdue(SupplierDelivery announcement) =>
        announcement.ExpectedDate is DateTime expected
        && expected.Date < DateTime.UtcNow.Date
        && announcement.Units.Any(u => u.State != ProductionUnitState.Arrived && u.State != ProductionUnitState.Delivered && u.State != ProductionUnitState.Cancelled);

    private async Task<string> RequireUserIdAsync() =>
        await currentUser.UserIdAsync() ?? throw new InvalidOperationException("No signed-in user.");

    private async Task RequireAdminOrOfficeAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office)) { return; }
        throw new InvalidOperationException("Only Admin or Office can do this.");
    }
}
