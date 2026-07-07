using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

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
            .Select(r => CanonicalJson.Deserialize<FurnitureModel>(r.BundleJson)
                ?? throw new InvalidOperationException($"Corrupt authoring model document '{r.ModelCode}'."))
            .ToList();
        return snapshot;
    }

    public async Task<FurnitureModel?> LoadModelAsync(string modelCode, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AuthoringModels.AsNoTracking().FirstOrDefaultAsync(m => m.ModelCode == modelCode, ct);
        return row is null ? null : CanonicalJson.Deserialize<FurnitureModel>(row.BundleJson);
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

    public async Task<IReadOnlyList<string>> ModelCodesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AuthoringModels.AsNoTracking().OrderBy(m => m.SortOrder).Select(m => m.ModelCode).ToListAsync(ct);
    }
}
