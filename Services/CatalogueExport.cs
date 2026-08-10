using System.Globalization;
using System.Text;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Export;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Catalogue redistribution exports for a PUBLISHED version. Render-only, UblExport idiom.
// CSV: the flat price list (element x fabric price group x market). JSON: the stored bundle
// written VERBATIM - byte-identical to what every order pins against, so a partner import
// can never disagree with our own pricing.
public sealed class CatalogueExport(IDbContextFactory<FurniturePlannerContext> factory, string outputRoot)
{
    public async Task<string> GenerateCsvAsync(string version, CancellationToken ct = default)
    {
        var snapshot = CanonicalJson.Deserialize<CatalogueSnapshot>(await BundleJsonAsync(version, ct))
            ?? throw new InvalidOperationException($"Catalogue version '{version}' failed to deserialize.");
        var rows = CatalogueFlattener.Flatten(snapshot);
        var csv = new StringBuilder();
        csv.AppendLine("CatalogueVersion;ContentHash;ModelCode;ModelName;CollectionCode;ElementCode;ElementName;PriceGroupCode;MarketCode;Price");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(';',
                Escape(row.CatalogueVersion), Escape(row.ContentHash),
                Escape(row.ModelCode), Escape(row.ModelName), Escape(row.CollectionCode ?? ""),
                Escape(row.ElementCode), Escape(row.ElementName),
                Escape(row.PriceGroupCode), Escape(row.MarketCode),
                row.Price.ToString("0.00", CultureInfo.InvariantCulture)));
        }
        return await WriteAsync($"catalogue-{version}.csv", Encoding.UTF8.GetBytes(csv.ToString()), ct);
    }

    public async Task<string> GenerateJsonAsync(string version, CancellationToken ct = default)
    {
        var bundleJson = await BundleJsonAsync(version, ct);
        return await WriteAsync($"catalogue-{version}.json", Encoding.UTF8.GetBytes(bundleJson), ct);
    }

    private async Task<string> BundleJsonAsync(string version, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PublishedCatalogues.AsNoTracking()
            .Where(c => c.Version == version).Select(c => c.BundleJson).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Published catalogue version '{version}' not found.");
    }

    private async Task<string> WriteAsync(string fileName, byte[] payload, CancellationToken ct)
    {
        Directory.CreateDirectory(outputRoot);
        var filePath = Path.Combine(outputRoot, fileName);
        await File.WriteAllBytesAsync(filePath, payload, ct);
        return filePath;
    }

    // Quote a field only when the delimiter/quote/newline forces it - keeps codes untouched.
    private static string Escape(string field) =>
        field.Contains(';') || field.Contains('"') || field.Contains('\n')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
}
