using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// A purchase order for raw materials (foam, frame stock, cotton, fabric, misc) sent to one
// supplier - MaterialOrder is SupplierOrder's counterpart for in-house material needs, but unlike
// SupplierOrder it has no ProductionUnit to double as its line, so receipt applies straight to a
// stored MaterialOrderLine and, in the same SaveChanges, to a MaterialStock balance. Numbering
// mirrors PurchasingService's PO-{yyyy}- max-suffix pattern (not count-based: Drafts are
// deletable, so a count would collide with a still-live order).
public sealed class MaterialOrderService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser)
{
    public async Task<MaterialOrder> CreateDraftAsync(int supplierId, IReadOnlyList<MaterialOrderLine> lines, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        RequirePositiveQuantities(lines);
        await using var db = await factory.CreateDbContextAsync(ct);

        var prefix = $"MPO-{DateTime.UtcNow.Year}-";
        var numbersThisYear = await db.MaterialOrders.Where(o => o.Number.StartsWith(prefix)).Select(o => o.Number).ToListAsync(ct);
        var maxSuffix = 0;
        foreach (var number in numbersThisYear)
        {
            if (int.TryParse(number[prefix.Length..], out var suffix) && suffix > maxSuffix) { maxSuffix = suffix; }
        }

        var order = new MaterialOrder
        {
            Number = $"{prefix}{maxSuffix + 1:D4}",
            SupplierId = supplierId,
            CreatedByUserId = await RequireUserIdAsync(),
            CreatedAt = DateTime.UtcNow,
        };
        order.Lines.AddRange(lines);
        db.MaterialOrders.Add(order);
        await db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<List<MaterialOrder>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MaterialOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Lines)
            .OrderByDescending(o => o.Number).ToListAsync(ct);
    }

    public async Task<MaterialOrder?> GetAsync(int materialOrderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MaterialOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == materialOrderId, ct);
    }

    public async Task AddLineAsync(int materialOrderId, MaterialOrderLine line, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        RequirePositiveQuantities([line]);
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await RequireDraftAsync(db, materialOrderId, ct);
        order.Lines.Add(line);
        await db.SaveChangesAsync(ct);
    }

    // A zero/negative ordered quantity would never be receivable (ReceiveAsync rejects any receipt
    // against a zero remainder), and a Sent order is neither editable nor deletable - such a line
    // would permanently strand the order in Sent. Reject it on both entry points instead.
    private static void RequirePositiveQuantities(IReadOnlyList<MaterialOrderLine> lines)
    {
        if (lines.Any(l => l.QuantityOrdered <= 0))
        {
            throw new InvalidOperationException("Ordered quantity must be greater than zero.");
        }
    }

    public async Task RemoveLineAsync(int materialOrderId, int lineId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await RequireDraftAsync(db, materialOrderId, ct);
        var line = order.Lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException($"Line {lineId} not found on order {order.Number}.");
        order.Lines.Remove(line);
        await db.SaveChangesAsync(ct);
    }

    // Draft deletable whether empty or not - lines cascade (unlike SupplierOrder's empty-only
    // delete guard, MaterialOrderLine is a real stored row with nothing else pointing at it).
    public async Task DeleteDraftAsync(int materialOrderId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await RequireDraftAsync(db, materialOrderId, ct);
        db.MaterialOrders.Remove(order);
        await db.SaveChangesAsync(ct);
    }

    public async Task SendAsync(int materialOrderId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await RequireDraftAsync(db, materialOrderId, ct);
        if (order.Lines.Count == 0) { throw new InvalidOperationException($"Material order {order.Number} has no lines."); }
        order.State = MaterialOrderState.Sent;
        order.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetTheirReferenceAsync(int materialOrderId, string? theirReference, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.MaterialOrders.FirstOrDefaultAsync(o => o.Id == materialOrderId, ct)
            ?? throw new InvalidOperationException($"Material order {materialOrderId} not found.");
        // Sent or Completed - an order can auto-complete (last line fully received) before anyone
        // gets around to typing the supplier's confirmation ref in, so a fast-completing order must
        // still accept it (mirrors PurchasingService.SetTheirReferenceAsync).
        if (order.State is not (MaterialOrderState.Sent or MaterialOrderState.Completed))
        {
            throw new InvalidOperationException($"Material order {order.Number} has not been sent.");
        }
        order.TheirReference = string.IsNullOrWhiteSpace(theirReference) ? null : theirReference.Trim();
        await db.SaveChangesAsync(ct);
    }

    // Applies a (partial) receipt to one line and upserts the matching MaterialStock row in the
    // same SaveChanges - atomicity is binding: a crash between the two writes must never leave a
    // received line with no matching stock bump. TryComplete runs last, off the just-saved state.
    public async Task ReceiveAsync(int materialOrderId, int lineId, decimal quantity, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.MaterialOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == materialOrderId, ct)
            ?? throw new InvalidOperationException($"Material order {materialOrderId} not found.");
        if (order.State != MaterialOrderState.Sent) { throw new InvalidOperationException($"Material order {order.Number} has not been sent."); }
        var line = order.Lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException($"Line {lineId} not found on order {order.Number}.");
        if (quantity <= 0) { throw new InvalidOperationException("Received quantity must be greater than zero."); }
        var remainder = line.QuantityOrdered - line.QuantityReceived;
        if (quantity > remainder) { throw new InvalidOperationException($"Cannot receive {quantity} - only {remainder} remains on line {line.Code}."); }

        line.QuantityReceived += quantity;

        var stock = await db.MaterialStocks.FirstOrDefaultAsync(
            s => s.Kind == line.Kind && s.Code == line.Code && s.HardnessCode == line.HardnessCode, ct);
        if (stock is null)
        {
            stock = new MaterialStock { Kind = line.Kind, Code = line.Code, HardnessCode = line.HardnessCode, Amount = 0m };
            db.MaterialStocks.Add(stock);
        }
        stock.Amount += quantity;
        stock.UpdatedAt = DateTime.UtcNow;

        db.MaterialMovements.Add(new MaterialMovement
        {
            Kind = line.Kind,
            Code = line.Code,
            HardnessCode = line.HardnessCode,
            Quantity = quantity,
            Type = MaterialMovementType.Receipt,
            OccurredAt = DateTime.UtcNow,
            Reference = order.Number,
            UserId = await currentUser.UserIdAsync(),
        });

        TryComplete(order);
        await db.SaveChangesAsync(ct);
    }

    // Sole writer of the Sent -> Completed transition: every line must be fully received.
    private static void TryComplete(MaterialOrder order)
    {
        if (order.State == MaterialOrderState.Sent && order.Lines.All(l => l.QuantityReceived >= l.QuantityOrdered))
        {
            order.State = MaterialOrderState.Completed;
        }
    }

    private static async Task<MaterialOrder> RequireDraftAsync(FurniturePlannerContext db, int materialOrderId, CancellationToken ct)
    {
        var order = await db.MaterialOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == materialOrderId, ct)
            ?? throw new InvalidOperationException($"Material order {materialOrderId} not found.");
        if (order.State != MaterialOrderState.Draft) { throw new InvalidOperationException($"Material order {order.Number} is not a draft."); }
        return order;
    }

    private async Task<string> RequireUserIdAsync() =>
        await currentUser.UserIdAsync() ?? throw new InvalidOperationException("No signed-in user.");

    private async Task RequireAdminOrOfficeAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office)) { return; }
        throw new InvalidOperationException("Only Admin or Office can do this.");
    }
}
