using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// One-time startup conversion: the OE1AbsorbVariantNamings migration renames the old VariantNamings
// table to LegacyVariantNamings (EF no longer maps it); this absorber — run right after
// Database.Migrate() — reads any staged rows via raw ADO, converts each into a catalogue-backed
// Article (assigned code + provenance parsed from the variant code), saves the articles document,
// and drops the staging table. Idempotent: no staging table means nothing to do.
public sealed class VariantNamingAbsorber(IDbContextFactory<FurniturePlannerContext> factory, AuthoringCatalogueStore store)
{
    public async Task AbsorbAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='LegacyVariantNamings';";
            if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) == 0) { return; }
        }

        var staged = new List<(string ModelCode, string VariantCode, string AssignedCode)>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT ModelCode, VariantCode, AssignedCode FROM LegacyVariantNamings;";
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { staged.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2))); }
        }

        if (staged.Count > 0)
        {
            var states = await db.ModelStates.AsNoTracking().ToDictionaryAsync(s => s.ModelCode, s => s.State, ct);
            var articles = await store.LoadArticlesAsync(ct);
            var nextId = articles.Count == 0 ? 1 : articles.Max(a => a.Id) + 1;
            foreach (var (modelCode, variantCode, assignedCode) in staged)
            {
                if (articles.Any(a => a.ModelCode == modelCode && a.VariantCode == variantCode)) { continue; }
                // Variant codes are "elementCode" or "elementCode-DEF:CHOICE-..."; element codes
                // cannot contain '-', and option/choice codes cannot contain '-' or ':'.
                var segments = variantCode.Split('-');
                articles.Add(new Article
                {
                    Id = nextId++,
                    AssignedCode = assignedCode,
                    ModelCode = modelCode,
                    ElementCode = segments[0],
                    VariantCode = variantCode,
                    Selections = segments.Skip(1).Select(s => s.Split(':', 2))
                        .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : ""),
                    State = states.TryGetValue(modelCode, out var state) ? state : TradeItemState.Draft,
                });
            }
            await store.SaveArticlesAsync(articles, ct);
        }

        await db.Database.ExecuteSqlRawAsync("DROP TABLE LegacyVariantNamings;", ct);
    }
}
