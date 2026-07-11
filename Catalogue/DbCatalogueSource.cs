using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Catalogue;

public sealed class DbCatalogueSource(IDbContextFactory<FurniturePlannerContext> factory, Func<DateTime>? clock = null) : ICatalogueSource
{
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);
    private CatalogueSnapshot? _cached;
    private DateTime? _reresolveAfter;   // earliest future EffectiveDate at resolution time; re-resolve once now passes it
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CatalogueSnapshot> GetCurrentAsync()
    {
        if (_cached is not null && (_reresolveAfter is null || _now() < _reresolveAfter))
        {
            return _cached;
        }
        await _gate.WaitAsync();
        try
        {
            var now = _now();
            if (_cached is not null && (_reresolveAfter is null || now < _reresolveAfter))
            {
                return _cached;
            }
            await using var context = await factory.CreateDbContextAsync();
            var rows = await context.PublishedCatalogues.AsNoTracking().ToListAsync();
            var effective = rows
                .Where(c => c.EffectiveDate <= now)
                .OrderByDescending(c => c.EffectiveDate)
                .ThenByDescending(c => int.TryParse(c.Version, out var parsed) ? parsed : 0)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("No published catalogue is effective.");
            var snapshot = CanonicalJson.Deserialize<CatalogueSnapshot>(effective.BundleJson)
                ?? throw new InvalidOperationException($"Catalogue version {effective.Version} failed to deserialize.");
            _cached = snapshot;
            _reresolveAfter = rows.Where(c => c.EffectiveDate > now).Select(c => (DateTime?)c.EffectiveDate).OrderBy(d => d).FirstOrDefault();
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _cached = null;
        _reresolveAfter = null;
    }
}
