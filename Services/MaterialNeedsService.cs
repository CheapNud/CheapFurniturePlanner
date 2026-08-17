using System.Globalization;
using System.Text;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// SuggestedToOrder is the ROUNDED figure (raw need rounded up to the preferred term's
// MinimumOrderQuantity, then up to a whole UnitsPerPackage multiple) - the manual edit a user makes
// before PO creation starts from this rounded value, not the raw one (planning math task 3).
public sealed record MaterialForecastRow(MaterialKind Kind, string Code, string? HardnessCode, string DisplayName,
    decimal GrossNeed, decimal InStock, decimal StockAfterNeeds, decimal OnOrder, decimal SuggestedToOrder,
    decimal MinimumStock, decimal? AverageUsagePerWeek, bool AverageUsageIsOverride, bool BelowMinimum,
    DateTime? OrderByDate, bool OrderByOverdue, int? PreferredSupplierId, string? PreferredSupplierName,
    decimal? UnitPrice, decimal? EstimatedCost);

// UnpinnedUnitCodes: in-house-resolved units whose order carries no PinnedCatalogueVersion, so
// there's no snapshot to resolve MaterialRequirements against - skipped from Rows the same way
// FinishAsync would throw if asked to backflush one (Materials 1: previously silent, only visible
// by a gross-need number that quietly excluded them).
public sealed record MaterialForecast(IReadOnlyList<MaterialForecastRow> Rows, IReadOnlyList<string> UnresolvedModelCodes, IReadOnlyList<string> UnpinnedUnitCodes);

// The in-house counterpart to PurchasingService's supplier sweep: instead of grouping Expected
// units by external supplier, it sums their pinned-snapshot material needs (MaterialRequirements,
// T1) for the units whose line resolves to the null-supplier "in-house" map marker
// (PartyService.MarkModelInHouseAsync) - same three-state rule as the sweep, but a dropship-pinned
// or externally-mapped unit is simply someone else's problem here, so it's excluded silently
// rather than counted as unresolved. Only a genuinely unmapped model code is unresolved.
public sealed class MaterialNeedsService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser,
    PinnedCatalogueProvider pinnedCatalogueProvider, string outputRoot, Func<DateTime>? clock = null)
{
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);

    public async Task<MaterialForecast> ComputeAsync(CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);

        var modelCodeToSupplierId = await db.SupplierModelMaps.AsNoTracking().ToDictionaryAsync(m => m.ModelCode, m => m.SupplierId, ct);

        // Materials 2: a StandaloneArticle line has no ModelCode/BOM to resolve material needs
        // against - excluded here at the join, same as PurchasingService's sweep excludes it from
        // material grouping. Unlike that sweep, this forecast never lists a standalone-unresolved
        // unit in UnresolvedModelCodes either (PurchasingService.SweepResult does, via AssignedCode
        // fallback) - standalone units are simply out of scope for a material forecast, not
        // unresolved (the page carries the same note next to the unresolved warning).
        var candidates = await db.ProductionUnits.AsNoTracking()
            .Where(u => u.State == ProductionUnitState.Expected)
            .Join(db.OrderLines.AsNoTracking().Where(l => l.Kind == OrderLineKind.ConfiguredElement), u => u.OrderLineId, l => l.Id,
                (u, l) => new { u.UnitCode, l.SupplierId, l.ModelCode, l.ElementCode, l.SelectionsJson, l.FabricColorCode, l.OrderId })
            .ToListAsync(ct);

        var orderIds = candidates.Select(c => c.OrderId).Distinct().ToList();
        // Carries PromisedDeliveryDate alongside the pinned version now - order-by date derivation
        // (planning math task 3) needs the same orders this loop already fetches.
        var orderInfo = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => (o.PinnedCatalogueVersion, o.PromisedDeliveryDate), ct);

        var unresolvedModelCodes = new SortedSet<string>(StringComparer.Ordinal);
        var inHouse = new List<(string UnitCode, string ModelCode, string ElementCode, string SelectionsJson, string? FabricColorCode, string? PinnedVersion, int OrderId)>();
        foreach (var candidate in candidates)
        {
            if (candidate.SupplierId is not null) { continue; } // dropship-pinned - someone else's problem
            if (candidate.ModelCode is null) { continue; } // configured line invariant, guard only
            if (!modelCodeToSupplierId.TryGetValue(candidate.ModelCode, out var mappedSupplierId))
            {
                unresolvedModelCodes.Add(candidate.ModelCode);
                continue;
            }
            if (mappedSupplierId is not null) { continue; } // mapped to an external supplier - excluded silently
            inHouse.Add((candidate.UnitCode, candidate.ModelCode, candidate.ElementCode!, candidate.SelectionsJson, candidate.FabricColorCode,
                orderInfo.GetValueOrDefault(candidate.OrderId).PinnedCatalogueVersion, candidate.OrderId));
        }

        // Materials 1: a unit whose order carries no pinned catalogue version has no snapshot to
        // resolve MaterialRequirements against - the same seam ProductionUnitService.FinishAsync
        // would hit (ApplyBackflushAsync fails hard there instead of skipping). Surfaced here as its
        // own list rather than thrown, since a forecast sweep must keep going for every OTHER unit.
        var unpinnedUnitCodes = new SortedSet<string>(StringComparer.Ordinal);
        var needs = new Dictionary<(MaterialKind Kind, string Code, string? HardnessCode), decimal>();
        var displayNames = new Dictionary<(MaterialKind Kind, string Code, string? HardnessCode), string>();
        // Orders whose units demand each material identity - feeds the order-by date (earliest
        // PromisedDeliveryDate among these, minus the preferred term's lead time).
        var demandingOrderIds = new Dictionary<(MaterialKind Kind, string Code, string? HardnessCode), HashSet<int>>();
        foreach (var group in inHouse.GroupBy(c => c.PinnedVersion))
        {
            if (group.Key is null)
            {
                foreach (var unit in group) { unpinnedUnitCodes.Add(unit.UnitCode); }
                continue;
            }
            var snapshot = await pinnedCatalogueProvider.GetAsync(group.Key, ct);
            foreach (var unit in group)
            {
                var selections = CanonicalJson.Deserialize<Dictionary<string, string>>(unit.SelectionsJson) ?? [];
                var lines = MaterialRequirements.Resolve(snapshot, unit.ModelCode, unit.ElementCode, selections, unit.FabricColorCode);
                foreach (var line in lines)
                {
                    var key = (line.Kind, line.Code, line.HardnessCode);
                    needs[key] = needs.GetValueOrDefault(key) + line.Quantity;
                    if (!displayNames.ContainsKey(key)) { displayNames[key] = ResolveDisplayName(snapshot, line.Kind, line.Code); }
                    if (!demandingOrderIds.TryGetValue(key, out var orders)) { orders = []; demandingOrderIds[key] = orders; }
                    orders.Add(unit.OrderId);
                }
            }
        }

        var stocks = await db.MaterialStocks.AsNoTracking()
            .ToDictionaryAsync(s => (s.Kind, s.Code, s.HardnessCode), s => s.Amount, ct);
        var onOrder = await db.MaterialOrderLines.AsNoTracking()
            .Join(db.MaterialOrders.AsNoTracking().Where(o => o.State == MaterialOrderState.Draft || o.State == MaterialOrderState.Sent),
                l => l.MaterialOrderId, o => o.Id, (l, o) => l)
            .GroupBy(l => new { l.Kind, l.Code, l.HardnessCode })
            .Select(g => new { g.Key, Remainder = g.Sum(l => l.QuantityOrdered - l.QuantityReceived) })
            .ToDictionaryAsync(g => (g.Key.Kind, g.Key.Code, g.Key.HardnessCode), g => g.Remainder, ct);

        // Demand-side knobs (MinimumStock, AverageUsageOverride) - profile-less materials keep the
        // SP-2 defaults (0, no override), so a material never authored in the Profile dialog forecasts
        // exactly as it did before this task.
        var profiles = await db.MaterialProfiles.AsNoTracking()
            .ToDictionaryAsync(p => (p.Kind, p.Code, p.HardnessCode), p => p, ct);
        // Exactly one preferred term per material identity (MaterialPlanningService's invariant) -
        // supply-side knobs (lead time, MOQ, package, price, supplier) all read from it; a material
        // with no terms at all has no preferred row and every one of these stays at its default.
        var preferredTerms = await db.MaterialSupplierTerms.AsNoTracking().Include(t => t.Supplier)
            .Where(t => t.IsPreferred)
            .ToDictionaryAsync(t => (t.Kind, t.Code, t.HardnessCode), t => t, ct);
        // Only consumption-typed movements ever feed the average (Receipt/Adjustment are excluded by
        // construction, not filtered out below) - loaded once per material identity so the trailing
        // window sum only has to slice a short in-memory list per row.
        var consumptionMovements = await db.MaterialMovements.AsNoTracking()
            .Where(m => m.Type == MaterialMovementType.Backflush || m.Type == MaterialMovementType.BackflushUndo)
            .ToListAsync(ct);
        var consumptionByMaterial = consumptionMovements
            .GroupBy(m => (m.Kind, m.Code, m.HardnessCode))
            .ToDictionary(g => g.Key, g => g.ToList());
        var windowStart = _now().AddDays(-56);
        var today = _now().Date;

        // Final-review fix: rows previously existed only for positive gross need, so a profiled or
        // termed material drained below MinimumStock with zero current demand never fired its
        // reorder point. Union in every identity carrying a profile or supplier term - the
        // suggested>0 filter below (applied only to the zero-demand ones) keeps a term/profile alone
        // from listing a material nobody needs and that's already stocked past its minimum (SP-2
        // parity: an identity with neither stays out entirely, same as before).
        var candidateKeys = new HashSet<(MaterialKind Kind, string Code, string? HardnessCode)>(
            needs.Where(kv => kv.Value > 0m).Select(kv => kv.Key));
        candidateKeys.UnionWith(profiles.Keys);
        candidateKeys.UnionWith(preferredTerms.Keys);

        var rows = candidateKeys.Select(key =>
        {
            var (kind, code, hardnessCode) = key;
            var grossNeed = needs.GetValueOrDefault(key);
            var inStock = stocks.GetValueOrDefault(key);
            var onOrderQty = onOrder.GetValueOrDefault(key);
            var profile = profiles.GetValueOrDefault(key);
            var minimumStock = profile?.MinimumStock ?? 0m;
            var preferredTerm = preferredTerms.GetValueOrDefault(key);

            // Computed average usage (per week): -(Backflush + BackflushUndo sums over the trailing
            // 56 days) / 8. A material with no consumption movement AT ALL (ever, not just in the
            // window) has never had its usage tracked - shown as null (no data) rather than a
            // misleading computed 0. One that has history but nothing in the last 56 days genuinely
            // is 0 (window correctly excludes the old rows). AverageUsageOverride always wins.
            bool averageUsageIsOverride;
            decimal? averageUsagePerWeek;
            if (profile?.AverageUsageOverride is decimal overrideValue)
            {
                averageUsagePerWeek = overrideValue;
                averageUsageIsOverride = true;
            }
            else
            {
                averageUsageIsOverride = false;
                averageUsagePerWeek = consumptionByMaterial.TryGetValue(key, out var movementsForMaterial)
                    ? -movementsForMaterial.Where(m => m.OccurredAt >= windowStart).Sum(m => m.Quantity) / 8m
                    : null;
            }

            var belowMinimum = inStock + onOrderQty - grossNeed < minimumStock;

            var rawSuggestion = Math.Max(0m, grossNeed + minimumStock - inStock - onOrderQty);
            var afterMoq = preferredTerm?.MinimumOrderQuantity is decimal moq ? Math.Ceiling(rawSuggestion / moq) * moq : rawSuggestion;
            var suggested = preferredTerm?.UnitsPerPackage is decimal package ? Math.Ceiling(afterMoq / package) * package : afterMoq;

            // Earliest promise among the demanding orders, minus the preferred term's lead time - no
            // term means no lead time to subtract, so no order-by date at all (not even an unadjusted
            // promise date - that would invent an urgency the material's supply side never committed to).
            DateTime? orderByDate = null;
            if (preferredTerm is not null && demandingOrderIds.TryGetValue(key, out var orderIds))
            {
                var promisedDates = orderIds.Select(id => orderInfo.GetValueOrDefault(id).PromisedDeliveryDate)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (promisedDates.Count > 0) { orderByDate = promisedDates.Min().AddDays(-preferredTerm.DeliveryTimeDays); }
            }
            var orderByOverdue = orderByDate is DateTime orderBy && orderBy.Date < today;

            var unitPrice = preferredTerm?.UnitPrice;
            var estimatedCost = unitPrice is decimal price ? suggested * price : (decimal?)null;

            return new MaterialForecastRow(kind, code, hardnessCode, displayNames.GetValueOrDefault(key, code),
                grossNeed, inStock, inStock - grossNeed, onOrderQty, suggested,
                minimumStock, averageUsagePerWeek, averageUsageIsOverride, belowMinimum,
                orderByDate, orderByOverdue, preferredTerm?.SupplierId, preferredTerm?.Supplier?.Name,
                unitPrice, estimatedCost);
        })
        .Where(row => row.GrossNeed > 0m || row.SuggestedToOrder > 0m)
        .OrderBy(r => r.Kind).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();

        return new MaterialForecast(rows, unresolvedModelCodes.ToList(), unpinnedUnitCodes.ToList());
    }

    // Read-only, like MaterialOrderService.ListAsync/GetAsync - no role check, the /materials page
    // itself is already Admin-or-Office gated.
    public async Task<List<MaterialStock>> StockAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var stocks = await db.MaterialStocks.AsNoTracking().ToListAsync(ct);
        return stocks.OrderBy(s => s.Kind).ThenBy(s => s.Code, StringComparer.Ordinal).ToList();
    }

    // Sets the balance to an absolute value (not a delta) - the StockAdjustDialog idiom is "this is
    // what's actually on the shelf", a correction, not a receipt. Upserts by (Kind, Code,
    // HardnessCode), same key ReceiveAsync uses.
    public async Task AdjustStockAsync(MaterialKind kind, string code, string? hardnessCode, decimal newAmount, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var stock = await db.MaterialStocks.FirstOrDefaultAsync(s => s.Kind == kind && s.Code == code && s.HardnessCode == hardnessCode, ct);
        var oldAmount = stock?.Amount ?? 0m;
        if (stock is null)
        {
            stock = new MaterialStock { Kind = kind, Code = code, HardnessCode = hardnessCode, Amount = newAmount, UpdatedAt = DateTime.UtcNow };
            db.MaterialStocks.Add(stock);
        }
        else
        {
            stock.Amount = newAmount;
            stock.UpdatedAt = DateTime.UtcNow;
        }

        db.MaterialMovements.Add(new MaterialMovement
        {
            Kind = kind,
            Code = code,
            HardnessCode = hardnessCode,
            Quantity = newAmount - oldAmount,
            Type = MaterialMovementType.Adjustment,
            OccurredAt = _now(),
            Reference = null,
            UserId = await currentUser.UserIdAsync(),
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<string> ExportCsvAsync(MaterialForecast forecast, CancellationToken ct = default)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Kind;Code;Hardness;Name;GrossNeed;InStock;StockAfterNeeds;OnOrder;SuggestedToOrder");
        foreach (var row in forecast.Rows)
        {
            csv.AppendLine(string.Join(';',
                row.Kind.ToString(), Escape(row.Code), Escape(row.HardnessCode ?? ""), Escape(row.DisplayName),
                row.GrossNeed.ToString(CultureInfo.InvariantCulture), row.InStock.ToString(CultureInfo.InvariantCulture),
                row.StockAfterNeeds.ToString(CultureInfo.InvariantCulture), row.OnOrder.ToString(CultureInfo.InvariantCulture),
                row.SuggestedToOrder.ToString(CultureInfo.InvariantCulture)));
        }
        Directory.CreateDirectory(outputRoot);
        var filePath = Path.Combine(outputRoot, $"material-needs-{_now():yyyyMMdd-HHmmss}.csv");
        await File.WriteAllBytesAsync(filePath, Encoding.UTF8.GetBytes(csv.ToString()), ct);
        return filePath;
    }

    // FrameBody carries no display name (Domain/Masters/FrameBody.cs) - falls back to the code
    // itself, same as any material code the masters don't otherwise know about. Fabric resolves
    // against the fabric groups' colour masters (FabricColor.Name), not the plain Materials list.
    private static string ResolveDisplayName(CatalogueSnapshot snapshot, MaterialKind kind, string code) => kind switch
    {
        MaterialKind.Frame => code,
        MaterialKind.Fabric => snapshot.FabricGroups.SelectMany(g => g.Colors).FirstOrDefault(c => c.Code == code)?.Name ?? code,
        _ => snapshot.Materials.FirstOrDefault(m => m.Code == code)?.Name ?? code,
    };

    // Quote a field only when the delimiter/quote/newline forces it - keeps codes untouched (CatalogueExport idiom).
    private static string Escape(string field) =>
        field.Contains(';') || field.Contains('"') || field.Contains('\n')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;

    private async Task RequireAdminOrOfficeAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office)) { return; }
        throw new InvalidOperationException("Only Admin or Office can do this.");
    }
}
