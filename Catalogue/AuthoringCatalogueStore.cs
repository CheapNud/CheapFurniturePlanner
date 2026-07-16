using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CheapFurniturePlanner.Catalogue;

public sealed class AuthoringCatalogueStore(IDbContextFactory<FurniturePlannerContext> factory)
{
    public async Task<bool> IsSeededAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AuthoringMasters.AnyAsync(ct) || await db.AuthoringModels.AnyAsync(ct);
    }

    // Split a full snapshot into a masters doc (Models=[], Version/ContentHash cleared — those are
    // publish-managed, not authoring content) + one model doc per model, SortOrder = array index.
    public async Task SeedFromAsync(CatalogueSnapshot seed, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var masters = new CatalogueSnapshot
        {
            Version = "",
            ContentHash = "",
            Models = [],
            PriceGroups = seed.PriceGroups,
            FabricGroups = seed.FabricGroups,
            Operations = seed.Operations,
            FrameBodies = seed.FrameBodies,
            Materials = seed.Materials,
            SprayPrices = seed.SprayPrices,
            FixedSurcharges = seed.FixedSurcharges,
            ChoiceSurcharges = seed.ChoiceSurcharges,
            CombinationPriceRules = seed.CombinationPriceRules,
            Markets = seed.Markets,
        };
        db.AuthoringMasters.Add(new AuthoringMastersDocument { BundleJson = CanonicalJson.Serialize(masters) });
        for (var i = 0; i < seed.Models.Count; i++)
        {
            db.AuthoringModels.Add(new AuthoringModelDocument
            {
                ModelCode = seed.Models[i].Code,
                SortOrder = i,
                BundleJson = CanonicalJson.Serialize(seed.Models[i]),
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<CatalogueSnapshot> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var mastersRow = await db.AuthoringMasters.AsNoTracking().FirstOrDefaultAsync(ct);
        var snapshot = mastersRow is null
            ? new CatalogueSnapshot { Version = "" }
            : CanonicalJson.Deserialize<CatalogueSnapshot>(mastersRow.BundleJson)
              ?? throw new InvalidOperationException("Corrupt authoring masters document.");
        var modelRows = await db.AuthoringModels.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync(ct);
        snapshot.Models = modelRows
            .Select(r => DeserializeModel(r.ModelCode, r.BundleJson))
            .ToList();
        snapshot.Articles = await LoadArticlesAsync(ct);
        return snapshot;
    }

    public async Task<FurnitureModel?> LoadModelAsync(string modelCode, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AuthoringModels.AsNoTracking().FirstOrDefaultAsync(m => m.ModelCode == modelCode, ct);
        if (row is null) return null;
        return DeserializeModel(modelCode, row.BundleJson);
    }

    // Wraps both the null-deserialize case and actually-malformed JSON (JsonException) in the same
    // InvalidOperationException so callers get a consistent, model-identified corrupt-document error
    // regardless of which way the document is broken.
    private static FurnitureModel DeserializeModel(string modelCode, string json)
    {
        try
        {
            return CanonicalJson.Deserialize<FurnitureModel>(json)
                ?? throw new InvalidOperationException($"Corrupt authoring model document '{modelCode}'.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Corrupt authoring model document '{modelCode}'.", ex);
        }
    }

    public async Task SaveModelAsync(FurnitureModel model, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AuthoringModels.FirstOrDefaultAsync(m => m.ModelCode == model.Code, ct);
        var json = CanonicalJson.Serialize(model);
        if (row is null)
        {
            var nextOrder = (await db.AuthoringModels.AnyAsync(ct)) ? await db.AuthoringModels.MaxAsync(m => m.SortOrder, ct) + 1 : 0;
            db.AuthoringModels.Add(new AuthoringModelDocument { ModelCode = model.Code, SortOrder = nextOrder, BundleJson = json });
        }
        else { row.BundleJson = json; }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteModelAsync(string modelCode, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AuthoringModels.FirstOrDefaultAsync(m => m.ModelCode == modelCode, ct);
        if (row is not null) { db.AuthoringModels.Remove(row); await db.SaveChangesAsync(ct); }
    }

    // Upserts the single working-masters document. The masters doc holds only master lists, so Models
    // is cleared before serialization (model docs live in AuthoringModels); Articles is cleared too
    // (its own document, AuthoringArticles); Version/ContentHash are publish-time metadata and are
    // zeroed too, matching SeedFromAsync's masters write, so a caller passing a stamped snapshot can't
    // persist stale metadata. This is the working-copy write P2's price editor uses; publishing
    // snapshots the working copy into a versioned catalogue.
    public async Task SaveMastersAsync(CatalogueSnapshot masters, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Persist a masters-only view without mutating the caller's instance: snapshot the fields we
        // zero for storage, serialize, then restore — so a caller that reuses `masters` afterward
        // still has its Models/Articles/Version/ContentHash intact.
        var savedModels = masters.Models;
        var savedArticles = masters.Articles;
        var savedVersion = masters.Version;
        var savedHash = masters.ContentHash;
        masters.Models = [];
        masters.Articles = [];
        masters.Version = "";
        masters.ContentHash = "";
        string json;
        try { json = CanonicalJson.Serialize(masters); }
        finally
        {
            masters.Models = savedModels;
            masters.Articles = savedArticles;
            masters.Version = savedVersion;
            masters.ContentHash = savedHash;
        }
        var row = await db.AuthoringMasters.FirstOrDefaultAsync(ct);
        if (row is null) { db.AuthoringMasters.Add(new AuthoringMastersDocument { BundleJson = json }); }
        else { row.BundleJson = json; }
        await db.SaveChangesAsync(ct);
    }

    // The single articles document: all Articles (catalogue-backed + standalone) as one JSON list,
    // mirroring the masters doc. Absent doc == empty catalogue (SeedFromAsync does not create it;
    // the first SaveArticlesAsync does).
    public async Task<List<Article>> LoadArticlesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AuthoringArticles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (row is null) { return []; }
        return CanonicalJson.Deserialize<List<Article>>(row.BundleJson)
            ?? throw new InvalidOperationException("Corrupt authoring articles document.");
    }

    public async Task SaveArticlesAsync(List<Article> articles, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var json = CanonicalJson.Serialize(articles);
        var row = await db.AuthoringArticles.FirstOrDefaultAsync(ct);
        if (row is null) { db.AuthoringArticles.Add(new AuthoringArticlesDocument { BundleJson = json }); }
        else { row.BundleJson = json; }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ModelCodesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AuthoringModels.AsNoTracking().OrderBy(m => m.SortOrder).Select(m => m.ModelCode).ToListAsync(ct);
    }
}
