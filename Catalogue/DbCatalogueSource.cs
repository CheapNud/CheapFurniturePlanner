using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Catalogue;

public sealed class DbCatalogueSource(IDbContextFactory<FurniturePlannerContext> factory) : ICatalogueSource
{
    private CatalogueSnapshot? _cached;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CatalogueSnapshot> GetCurrentAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }
        await _gate.WaitAsync();
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }
            await using var context = await factory.CreateDbContextAsync();
            var row = await context.PublishedCatalogues.AsNoTracking().FirstOrDefaultAsync(c => c.IsCurrent)
                ?? throw new InvalidOperationException("No published catalogue is current.");
            var snapshot = CanonicalJson.Deserialize<CatalogueSnapshot>(row.BundleJson)
                ?? throw new InvalidOperationException($"Catalogue version {row.Version} failed to deserialize.");
            _cached = snapshot;
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _cached = null;
}
