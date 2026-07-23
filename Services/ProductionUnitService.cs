using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public enum ProductionPhase { InProduction, Ready, Delivered }
public enum ScanOutcome { Arrived, AlreadyArrived, Unknown }

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

    public async Task<ScanOutcome> ArriveByCodeAsync(string unitCode, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var trimmedCode = (unitCode ?? "").Trim();
        var unit = await db.ProductionUnits.FirstOrDefaultAsync(u => u.UnitCode == trimmedCode, ct);
        if (unit is null || unit.State is ProductionUnitState.Delivered or ProductionUnitState.Cancelled) { return ScanOutcome.Unknown; }
        if (unit.State == ProductionUnitState.Arrived) { return ScanOutcome.AlreadyArrived; }
        Arrive(unit);
        await db.SaveChangesAsync(ct);
        return ScanOutcome.Arrived;
    }

    public async Task ArriveAsync(int unitId, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var unit = await RequireUnitAsync(db, unitId, ct);
        if (unit.State != ProductionUnitState.Expected) { throw new InvalidOperationException($"Unit {unit.UnitCode} is not expected."); }
        Arrive(unit);
        await db.SaveChangesAsync(ct);
    }

    public async Task UndoArriveAsync(int unitId, string? reviewNote = null, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var unit = await RequireUnitAsync(db, unitId, ct);
        if (unit.State != ProductionUnitState.Arrived) { throw new InvalidOperationException($"Unit {unit.UnitCode} is not arrived."); }
        if (unit.TripId is not null) { throw new InvalidOperationException($"Unit {unit.UnitCode} is loaded on a trip - release it first."); }
        unit.State = ProductionUnitState.Expected;
        unit.ArrivedAt = null;
        unit.ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? unit.ReviewNote : reviewNote.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task<Trip> CreateTripAsync(CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var prefix = $"TRP-{DateTime.UtcNow.Year}-";
        var codesThisYear = await db.Trips.Where(t => t.TripCode.StartsWith(prefix)).Select(t => t.TripCode).ToListAsync(ct);
        var maxSuffix = 0;
        foreach (var code in codesThisYear)
        {
            if (int.TryParse(code[prefix.Length..], out var suffix) && suffix > maxSuffix) { maxSuffix = suffix; }
        }
        var trip = new Trip { TripCode = $"{prefix}{maxSuffix + 1:D4}" };
        db.Trips.Add(trip);
        await db.SaveChangesAsync(ct);
        return trip;
    }

    public async Task UpdateTripAsync(int tripId, DateTime? departureDate, string? truckName, string? driverName, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var trip = await RequirePlanningTripAsync(db, tripId, ct);
        trip.DepartureDate = departureDate;
        trip.TruckName = string.IsNullOrWhiteSpace(truckName) ? null : truckName.Trim();
        trip.DriverName = string.IsNullOrWhiteSpace(driverName) ? null : driverName.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Trip>> ListTripsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Trips.AsNoTracking().Include(t => t.Units).OrderByDescending(t => t.TripCode).ToListAsync(ct);
    }

    public async Task<Trip?> GetTripAsync(int tripId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Trips.AsNoTracking()
            .Include(t => t.Units.OrderBy(u => u.LoadPosition)).ThenInclude(u => u.Order)!.ThenInclude(o => o!.Consumer)
            .FirstOrDefaultAsync(t => t.Id == tripId, ct);
    }

    public async Task<List<ProductionUnit>> AssignableUnitsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ProductionUnits.AsNoTracking()
            .Include(u => u.Order)!.ThenInclude(o => o!.Consumer)
            .Where(u => u.State == ProductionUnitState.Arrived && u.TripId == null)
            .OrderBy(u => u.UnitCode)
            .ToListAsync(ct);
    }

    public async Task AssignToTripAsync(int tripId, int unitId, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequirePlanningTripAsync(db, tripId, ct);
        var unit = await RequireUnitAsync(db, unitId, ct);
        if (unit.State != ProductionUnitState.Arrived) { throw new InvalidOperationException($"Unit {unit.UnitCode} has not arrived."); }
        if (unit.TripId is not null) { throw new InvalidOperationException($"Unit {unit.UnitCode} is already on a trip."); }
        unit.TripId = tripId;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseFromTripAsync(int unitId, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var unit = await RequireUnitAsync(db, unitId, ct);
        if (unit.TripId is int assignedTripId)
        {
            await RequirePlanningTripAsync(db, assignedTripId, ct);
            unit.TripId = null;
            unit.LoadPosition = null;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SetLoadPositionAsync(int unitId, int? loadPosition, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var unit = await RequireUnitAsync(db, unitId, ct);
        if (unit.TripId is not int assignedTripId) { throw new InvalidOperationException($"Unit {unit.UnitCode} is not on a trip."); }
        await RequirePlanningTripAsync(db, assignedTripId, ct);
        unit.LoadPosition = loadPosition;
        await db.SaveChangesAsync(ct);
    }

    public async Task DepartAsync(int tripId, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var trip = await db.Trips.Include(t => t.Units).FirstOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new InvalidOperationException($"Trip {tripId} not found.");
        if (trip.State != TripState.Planning) { throw new InvalidOperationException($"Trip {trip.TripCode} has already departed."); }
        if (trip.Units.Count == 0) { throw new InvalidOperationException($"Trip {trip.TripCode} has no units loaded."); }
        trip.State = TripState.Departed;
        trip.DepartedAt = DateTime.UtcNow;
        foreach (var unit in trip.Units)
        {
            unit.State = ProductionUnitState.Delivered;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteTripAsync(int tripId, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var trip = await db.Trips.Include(t => t.Units).FirstOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new InvalidOperationException($"Trip {tripId} not found.");
        if (trip.State != TripState.Planning || trip.Units.Count > 0) { throw new InvalidOperationException($"Trip {trip.TripCode} is not an empty planning trip."); }
        db.Trips.Remove(trip);
        await db.SaveChangesAsync(ct);
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

    private static void Arrive(ProductionUnit unit)
    {
        unit.State = ProductionUnitState.Arrived;
        unit.ArrivedAt = DateTime.UtcNow;
    }

    private static async Task<ProductionUnit> RequireUnitAsync(FurniturePlannerContext db, int unitId, CancellationToken ct) =>
        await db.ProductionUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct)
            ?? throw new InvalidOperationException($"Unit {unitId} not found.");

    private static async Task<Trip> RequirePlanningTripAsync(FurniturePlannerContext db, int tripId, CancellationToken ct)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new InvalidOperationException($"Trip {tripId} not found.");
        if (trip.State != TripState.Planning) { throw new InvalidOperationException($"Trip {trip.TripCode} has already departed."); }
        return trip;
    }

    private async Task RequireWarehouseStaffAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office) || await currentUser.IsInRoleAsync(Roles.Warehouse)) { return; }
        throw new InvalidOperationException("Only Admin, Office or Warehouse can do this.");
    }
}
