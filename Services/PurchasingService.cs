using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

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
        if (order.State != SupplierOrderState.Sent) { throw new InvalidOperationException($"Purchase order {order.PoNumber} has not been sent."); }
        order.TheirReference = string.IsNullOrWhiteSpace(theirReference) ? null : theirReference.Trim();
        await db.SaveChangesAsync(ct);
    }

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
                (u, l) => new { Unit = u, l.SupplierId, l.ModelCode })
            .ToListAsync(ct);

        var bySupplier = new Dictionary<int, List<ProductionUnit>>();
        var unresolvedModelCodes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var supplierId = candidate.SupplierId
                ?? (candidate.ModelCode is not null && modelCodeToSupplierId.TryGetValue(candidate.ModelCode, out var mappedSupplierId) ? mappedSupplierId : null);
            if (supplierId is null)
            {
                if (candidate.ModelCode is not null) { unresolvedModelCodes.Add(candidate.ModelCode); }
                continue;
            }
            if (!bySupplier.TryGetValue(supplierId.Value, out var units)) { bySupplier[supplierId.Value] = units = []; }
            units.Add(candidate.Unit);
        }
        return (bySupplier, unresolvedModelCodes.ToList());
    }

    private async Task<string> RequireUserIdAsync() =>
        await currentUser.UserIdAsync() ?? throw new InvalidOperationException("No signed-in user.");

    private async Task RequireAdminOrOfficeAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office)) { return; }
        throw new InvalidOperationException("Only Admin or Office can do this.");
    }
}
