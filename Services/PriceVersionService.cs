using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public enum PriceVersionStatus { Effective, Scheduled, Superseded }

public record PriceVersionInfo(string Version, DateTime EffectiveDate, DateTime PublishedAt, string ContentHash, PriceVersionStatus Status);

public sealed class PriceVersionService(IDbContextFactory<FurniturePlannerContext> factory, ModelPublishService publish)
{
    public async Task<IReadOnlyList<PriceVersionInfo>> ListVersionsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.PublishedCatalogues.AsNoTracking().ToListAsync(ct);
        var now = DateTime.UtcNow;
        var effective = rows
            .Where(c => c.EffectiveDate <= now)
            .OrderByDescending(c => c.EffectiveDate).ThenByDescending(c => Ver(c.Version))
            .FirstOrDefault();
        return rows
            .OrderByDescending(c => Ver(c.Version))
            .Select(c => new PriceVersionInfo(c.Version, c.EffectiveDate, c.PublishedAt, c.ContentHash,
                c.EffectiveDate > now ? PriceVersionStatus.Scheduled
                    : ReferenceEquals(c, effective) ? PriceVersionStatus.Effective
                    : PriceVersionStatus.Superseded))
            .ToList();
    }

    // Compares the working (Active-only) snapshot to the most recently published one on a version-
    // independent content signature, because ComputeContentHash bakes in Version (so raw stored hashes
    // always differ). Normalizing Version to "" on both sides makes it a true content comparison.
    public async Task<bool> HasPendingChangesAsync(CancellationToken ct = default)
    {
        var working = await publish.LoadActiveSnapshotAsync(ct);
        var workingSignature = ContentSignature(working);
        await using var db = await factory.CreateDbContextAsync(ct);
        // "Latest published" here is the most-recently-published bundle (max Id, append-only), NOT the
        // currently-effective one. This is deliberate: the banner asks "have I edited since my last
        // publish?" — after scheduling a future-dated version, the working copy matches that new bundle
        // (no pending), even though the served/effective version is still the older one.
        var latest = await db.PublishedCatalogues.AsNoTracking().OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct);
        if (latest is null) { return true; }
        var published = CanonicalJson.Deserialize<CatalogueSnapshot>(latest.BundleJson);
        if (published is null) { return true; }
        return workingSignature != ContentSignature(published);
    }

    public Task PublishNewVersionAsync(DateTime effectiveDate, CancellationToken ct = default)
        => publish.RepublishAsync(effectiveDate, ct);

    private static string ContentSignature(CatalogueSnapshot snapshot)
    {
        var savedVersion = snapshot.Version;
        snapshot.Version = "";
        try { return snapshot.ComputeContentHash(); }
        finally { snapshot.Version = savedVersion; }
    }

    private static int Ver(string version) => int.TryParse(version, out var parsed) ? parsed : 0;
}
