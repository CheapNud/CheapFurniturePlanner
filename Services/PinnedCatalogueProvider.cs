using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Fetches a published catalogue bundle by its pinned version. A missing version is a hard error —
// an order must never silently fall back to "current" (that would un-pin its prices). Caches per
// scoped instance: one order-editing circuit re-reads its pinned bundle many times.
public sealed class PinnedCatalogueProvider(IDbContextFactory<FurniturePlannerContext> factory)
{
    private readonly Dictionary<string, CatalogueSnapshot> _cache = [];

    public async Task<CatalogueSnapshot> GetAsync(string version, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(version, out var cached)) { return cached; }
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.PublishedCatalogues.AsNoTracking().FirstOrDefaultAsync(c => c.Version == version, ct)
            ?? throw new InvalidOperationException($"Pinned catalogue version '{version}' not found.");
        var snapshot = CanonicalJson.Deserialize<CatalogueSnapshot>(row.BundleJson)
            ?? throw new InvalidOperationException($"Corrupt catalogue bundle for version '{version}'.");
        _cache[version] = snapshot;
        return snapshot;
    }
}
