using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public enum ProductionPhase { InProduction, Ready, Delivered }

// Every unit/trip mutation lives here so the two state machines are enforced in one place.
// Spawn/backfill/cancel are cascade entry points invoked by order flows and the startup
// backfill (no signed-in user), so they carry no role guard; the dock actions (Task 3) do.
public sealed class ProductionUnitService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser)
{
    public async Task SpawnForOrderAsync(int orderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        if (order.State != OrderState.Placed) { return; }

        var existingLineIds = await db.ProductionUnits.Where(u => u.OrderId == orderId).Select(u => u.OrderLineId).Distinct().ToListAsync(ct);
        foreach (var line in order.Lines.Where(l => l.DeliverToWarehouse && !existingLineIds.Contains(l.Id)))
        {
            for (var sequence = 1; sequence <= line.Quantity; sequence++)
            {
                db.ProductionUnits.Add(new ProductionUnit
                {
                    OrderId = order.Id,
                    OrderLineId = line.Id,
                    SequenceNumber = sequence,
                    UnitCode = $"{order.OrderNumber}-{line.DisplayIndex + 1}-{sequence}",
                    CreatedAt = DateTime.UtcNow,
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task BackfillAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var placedOrderIds = await db.Orders.Where(o => o.State == OrderState.Placed).Select(o => o.Id).ToListAsync(ct);
        foreach (var orderId in placedOrderIds)
        {
            await SpawnForOrderAsync(orderId, ct);
        }
    }

    public async Task CancelForOrderAsync(int orderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var openUnits = await db.ProductionUnits
            .Where(u => u.OrderId == orderId && (u.State == ProductionUnitState.Expected || u.State == ProductionUnitState.Arrived))
            .ToListAsync(ct);
        foreach (var unit in openUnits)
        {
            unit.State = ProductionUnitState.Cancelled;
            unit.TripId = null;
            unit.LoadPosition = null;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ProductionUnit>> ListUnitsAsync(string? orderNumberFilter = null, ProductionUnitState? stateFilter = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var unitsQuery = db.ProductionUnits.AsNoTracking()
            .Include(u => u.Order)!.ThenInclude(o => o!.Consumer)
            .Include(u => u.Trip)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(orderNumberFilter))
        {
            unitsQuery = unitsQuery.Where(u => u.Order!.OrderNumber.Contains(orderNumberFilter.Trim()));
        }
        if (stateFilter is ProductionUnitState wantedState)
        {
            unitsQuery = unitsQuery.Where(u => u.State == wantedState);
        }
        return await unitsQuery.OrderBy(u => u.UnitCode).ToListAsync(ct);
    }

    public async Task<List<ProductionUnit>> UnitsForOrderAsync(int orderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ProductionUnits.AsNoTracking().Where(u => u.OrderId == orderId).OrderBy(u => u.UnitCode).ToListAsync(ct);
    }

    public async Task<ProductionPhase?> PhaseForOrderAsync(int orderId, CancellationToken ct = default) =>
        DerivePhase(await UnitsForOrderAsync(orderId, ct));

    public async Task<Dictionary<int, ProductionPhase?>> PhasesForOrdersAsync(IReadOnlyList<int> orderIds, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var units = await db.ProductionUnits.AsNoTracking().Where(u => orderIds.Contains(u.OrderId)).ToListAsync(ct);
        return orderIds.ToDictionary(id => id, id => DerivePhase(units.Where(u => u.OrderId == id).ToList()));
    }

    // The single phase-derivation implementation: over non-Cancelled units - none -> null;
    // any Expected -> InProduction; none Expected, >=1 not Delivered -> Ready; all Delivered -> Delivered.
    public static ProductionPhase? DerivePhase(IReadOnlyList<ProductionUnit> units)
    {
        var active = units.Where(u => u.State != ProductionUnitState.Cancelled).ToList();
        if (active.Count == 0) { return null; }
        if (active.Any(u => u.State == ProductionUnitState.Expected)) { return ProductionPhase.InProduction; }
        return active.All(u => u.State == ProductionUnitState.Delivered) ? ProductionPhase.Delivered : ProductionPhase.Ready;
    }
}
