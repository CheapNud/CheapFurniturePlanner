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
        var affectedTripIds = openUnits.Where(u => u.TripId is not null).Select(u => u.TripId!.Value).Distinct().ToList();
        // Units on a Draft PO are released (the PO is still just a candidate list); units on a
        // Sent PO stay linked - the PO was actually placed with the supplier, so cancelling the
        // order still leaves a real line on it, just a cancelled one.
        var linkedSupplierOrderIds = openUnits.Where(u => u.SupplierOrderId is not null).Select(u => u.SupplierOrderId!.Value).Distinct().ToList();
        var linkedOrderStates = linkedSupplierOrderIds.Count > 0
            ? await db.SupplierOrders.Where(o => linkedSupplierOrderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.State, ct)
            : [];
        foreach (var unit in openUnits)
        {
            unit.State = ProductionUnitState.Cancelled;
            unit.TripId = null;
            unit.LoadPosition = null;
            if (unit.SupplierOrderId is int poId && linkedOrderStates.GetValueOrDefault(poId) == SupplierOrderState.Draft) { unit.SupplierOrderId = null; }
        }
        await db.SaveChangesAsync(ct);

        if (affectedTripIds.Count > 0)
        {
            var affectedTrips = await db.Trips.Include(t => t.Units)
                .Where(t => affectedTripIds.Contains(t.Id) && t.State == TripState.Departed)
                .ToListAsync(ct);
            foreach (var trip in affectedTrips) { TryCompleteTrip(trip); }
            await db.SaveChangesAsync(ct);
        }

        var affectedSentOrderIds = linkedOrderStates.Where(kv => kv.Value == SupplierOrderState.Sent).Select(kv => kv.Key).ToList();
        if (affectedSentOrderIds.Count > 0)
        {
            var affectedSupplierOrders = await db.SupplierOrders.Include(o => o.Units)
                .Where(o => affectedSentOrderIds.Contains(o.Id))
                .ToListAsync(ct);
            foreach (var order in affectedSupplierOrders) { TryCompleteSupplierOrder(order); }
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<ProductionUnit>> ListUnitsAsync(string? orderNumberFilter = null, ProductionUnitState? stateFilter = null, int? supplierDeliveryId = null, CancellationToken ct = default)
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
        if (supplierDeliveryId is int wantedDeliveryId)
        {
            unitsQuery = unitsQuery.Where(u => u.SupplierDeliveryId == wantedDeliveryId);
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
        await CompleteSupplierOrderIfLinkedAsync(db, unit, ct);
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
        await CompleteSupplierOrderIfLinkedAsync(db, unit, ct);
        await db.SaveChangesAsync(ct);
    }

    // Loads the unit's purchase order (with Units) inside the same context/transaction as the
    // arrival mutation, so the just-arrived unit's state change is visible to the completion check
    // without a round trip - the tracked unit is the same instance the Units collection resolves to.
    private static async Task CompleteSupplierOrderIfLinkedAsync(FurniturePlannerContext db, ProductionUnit unit, CancellationToken ct)
    {
        if (unit.SupplierOrderId is not int supplierOrderId) { return; }
        var order = await db.SupplierOrders.Include(o => o.Units).FirstAsync(o => o.Id == supplierOrderId, ct);
        TryCompleteSupplierOrder(order);
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

    public async Task UpdateTripAsync(int tripId, DateTime? departureDate, string? truckName, string? driverName, int? regionId, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var trip = await RequirePlanningTripAsync(db, tripId, ct);
        trip.DepartureDate = departureDate;
        trip.TruckName = string.IsNullOrWhiteSpace(truckName) ? null : truckName.Trim();
        trip.DriverName = string.IsNullOrWhiteSpace(driverName) ? null : driverName.Trim();
        trip.RegionId = regionId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Trip>> ListTripsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Trips.AsNoTracking().Include(t => t.Units).Include(t => t.Region).OrderByDescending(t => t.TripCode).ToListAsync(ct);
    }

    public async Task<Trip?> GetTripAsync(int tripId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Trips.AsNoTracking()
            .Include(t => t.Units.OrderBy(u => u.LoadPosition)).ThenInclude(u => u.Order)!.ThenInclude(o => o!.Consumer)
            .Include(t => t.Units.OrderBy(u => u.LoadPosition)).ThenInclude(u => u.Order)!.ThenInclude(o => o!.DeliveryAddress)
            .Include(t => t.Region)
            .FirstOrDefaultAsync(t => t.Id == tripId, ct);
    }

    // regionId null means "every region" (the soft filter the dock uses before a trip has one
    // assigned); set, it keeps only units whose order ships to that region - no address means
    // never assignable to a region-scoped trip.
    public async Task<List<ProductionUnit>> AssignableUnitsAsync(int? regionId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var unitsQuery = db.ProductionUnits.AsNoTracking()
            .Include(u => u.Order)!.ThenInclude(o => o!.Consumer)
            .Include(u => u.Order)!.ThenInclude(o => o!.DeliveryAddress)!.ThenInclude(a => a!.Region)
            .Where(u => (u.State == ProductionUnitState.Expected || u.State == ProductionUnitState.Arrived) && u.TripId == null)
            .AsQueryable();
        if (regionId is int wantedRegionId)
        {
            unitsQuery = unitsQuery.Where(u => u.Order!.DeliveryAddress != null && u.Order.DeliveryAddress.RegionId == wantedRegionId);
        }
        return await unitsQuery.OrderBy(u => u.UnitCode).ToListAsync(ct);
    }

    public async Task AssignToTripAsync(int tripId, int unitId, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequirePlanningTripAsync(db, tripId, ct);
        var unit = await RequireUnitAsync(db, unitId, ct);
        if (unit.State is not (ProductionUnitState.Expected or ProductionUnitState.Arrived))
        {
            throw new InvalidOperationException($"Unit {unit.UnitCode} cannot be planned ({unit.State}).");
        }
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
        if (trip.State != TripState.Planning) { throw new InvalidOperationException($"Trip {trip.TripCode} is no longer in planning."); }
        if (trip.Units.Count == 0) { throw new InvalidOperationException($"Trip {trip.TripCode} has no units loaded."); }
        var notArrived = trip.Units.Where(u => u.State == ProductionUnitState.Expected).Select(u => u.UnitCode).ToList();
        if (notArrived.Count > 0)
        {
            throw new InvalidOperationException($"Trip {trip.TripCode} cannot depart - not yet arrived: {string.Join(", ", notArrived)}.");
        }
        trip.State = TripState.Departed;
        trip.DepartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmDeliveredAsync(int unitId, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var unit = await RequireUnitAsync(db, unitId, ct);
        var trip = await RequireDepartedTripAsync(db, unit, ct);
        unit.State = ProductionUnitState.Delivered;
        TryCompleteTrip(trip);
        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmFailedAsync(int unitId, string reason, CancellationToken ct = default)
    {
        await RequireWarehouseStaffAsync();
        if (string.IsNullOrWhiteSpace(reason)) { throw new InvalidOperationException("A failure reason is required."); }
        await using var db = await factory.CreateDbContextAsync(ct);
        var unit = await RequireUnitAsync(db, unitId, ct);
        var trip = await RequireDepartedTripAsync(db, unit, ct);
        unit.TripId = null;
        unit.LoadPosition = null;
        unit.ReviewNote = reason.Trim();
        trip.Units.Remove(unit);
        TryCompleteTrip(trip);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Trip> RequireDepartedTripAsync(FurniturePlannerContext db, ProductionUnit unit, CancellationToken ct)
    {
        if (unit.TripId is not int tripId) { throw new InvalidOperationException($"Unit {unit.UnitCode} is not on a trip."); }
        var trip = await db.Trips.Include(t => t.Units).FirstAsync(t => t.Id == tripId, ct);
        if (trip.State != TripState.Departed) { throw new InvalidOperationException($"Trip {trip.TripCode} is not out for delivery."); }
        if (unit.State != ProductionUnitState.Arrived) { throw new InvalidOperationException($"Unit {unit.UnitCode} is not awaiting confirmation."); }
        return trip;
    }

    private static void TryCompleteTrip(Trip trip)
    {
        if (trip.State == TripState.Departed && trip.Units.All(u => u.State == ProductionUnitState.Delivered))
        {
            trip.State = TripState.Completed;
            trip.CompletedAt = DateTime.UtcNow;
        }
    }

    // A Sent PO completes once nothing on it is still outstanding - Cancelled units never block it
    // (a unit can only be Cancelled while its Sent-PO link stays put, see CancelForOrderAsync).
    private static void TryCompleteSupplierOrder(SupplierOrder order)
    {
        if (order.State == SupplierOrderState.Sent && order.Units.All(u => u.State != ProductionUnitState.Expected))
        {
            order.State = SupplierOrderState.Completed;
        }
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

    // A trip's departure date only counts as missing the promise once it's actually later than the
    // day the consumer was promised - same-day departure is on time, and an unset promise or
    // departure date is nothing to warn about yet.
    public static bool PromiseMissed(DateTime? promised, DateTime? departure) =>
        promised is not null && departure is not null && departure.Value.Date > promised.Value.Date;

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
        if (trip.State != TripState.Planning) { throw new InvalidOperationException($"Trip {trip.TripCode} is no longer in planning."); }
        return trip;
    }

    private async Task RequireWarehouseStaffAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office) || await currentUser.IsInRoleAsync(Roles.Warehouse)) { return; }
        throw new InvalidOperationException("Only Admin, Office or Warehouse can do this.");
    }
}
