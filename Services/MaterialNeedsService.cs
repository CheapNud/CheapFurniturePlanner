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

public sealed record MaterialForecastRow(MaterialKind Kind, string Code, string? HardnessCode, string DisplayName,
    decimal GrossNeed, decimal InStock, decimal StockAfterNeeds, decimal OnOrder, decimal SuggestedToOrder);

public sealed record MaterialForecast(IReadOnlyList<MaterialForecastRow> Rows, IReadOnlyList<string> UnresolvedModelCodes);

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

        var candidates = await db.ProductionUnits.AsNoTracking()
            .Where(u => u.State == ProductionUnitState.Expected)
            .Join(db.OrderLines.AsNoTracking().Where(l => l.Kind == OrderLineKind.ConfiguredElement), u => u.OrderLineId, l => l.Id,
                (u, l) => new { l.SupplierId, l.ModelCode, l.ElementCode, l.SelectionsJson, l.FabricColorCode, l.OrderId })
            .ToListAsync(ct);

        var orderIds = candidates.Select(c => c.OrderId).Distinct().ToList();
        var pinnedVersions = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.PinnedCatalogueVersion, ct);

        var unresolvedModelCodes = new SortedSet<string>(StringComparer.Ordinal);
        var inHouse = new List<(string ModelCode, string ElementCode, string SelectionsJson, string? FabricColorCode, string? PinnedVersion)>();
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
            inHouse.Add((candidate.ModelCode, candidate.ElementCode!, candidate.SelectionsJson, candidate.FabricColorCode,
                pinnedVersions.GetValueOrDefault(candidate.OrderId)));
        }

        var needs = new Dictionary<(MaterialKind Kind, string Code, string? HardnessCode), decimal>();
        var displayNames = new Dictionary<(MaterialKind Kind, string Code, string? HardnessCode), string>();
        foreach (var group in inHouse.GroupBy(c => c.PinnedVersion))
        {
            if (group.Key is null) { continue; } // no pinned version to resolve against, guard only
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

        var rows = needs.Where(kv => kv.Value > 0m).Select(kv =>
        {
            var (kind, code, hardnessCode) = kv.Key;
            var grossNeed = kv.Value;
            var inStock = stocks.GetValueOrDefault(kv.Key);
            var onOrderQty = onOrder.GetValueOrDefault(kv.Key);
            return new MaterialForecastRow(kind, code, hardnessCode, displayNames.GetValueOrDefault(kv.Key, code),
                grossNeed, inStock, inStock - grossNeed, onOrderQty, Math.Max(0m, grossNeed - inStock - onOrderQty));
        })
        .OrderBy(r => r.Kind).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();

        return new MaterialForecast(rows, unresolvedModelCodes.ToList());
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
