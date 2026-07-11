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
            var candidates = await context.PublishedCatalogues.AsNoTracking()
                .Select(c => new { c.Id, c.Version, c.EffectiveDate })
                .ToListAsync();
            var effective = candidates
                .Where(c => c.EffectiveDate <= now)
                .OrderByDescending(c => c.EffectiveDate)
                .ThenByDescending(c => int.TryParse(c.Version, out var parsed) ? parsed : 0)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("No published catalogue is effective.");
            var bundleJson = await context.PublishedCatalogues.AsNoTracking()
                .Where(c => c.Id == effective.Id)
                .Select(c => c.BundleJson)
                .SingleAsync();
            var snapshot = CanonicalJson.Deserialize<CatalogueSnapshot>(bundleJson)
                ?? throw new InvalidOperationException($"Catalogue version {effective.Version} failed to deserialize.");
            _cached = snapshot;
            _reresolveAfter = candidates.Where(c => c.EffectiveDate > now).Select(c => (DateTime?)c.EffectiveDate).OrderBy(d => d).FirstOrDefault();
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
