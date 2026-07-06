using System.Reflection;
using System.Text.Json;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Domain.Tests.Golden;

// A named pricing scenario against the Fjord demo catalogue: which market, seller multiplier and
// configuration to price. Backed by the embedded Golden/golden-cases.json manifest.
public record GoldenCase(string Name, string Market, decimal SellerMultiplier, ProductConfiguration Configuration);

public static class GoldenCaseLoader
{
    private static readonly JsonSerializerOptions Options = new();

    public static IReadOnlyList<GoldenCase> LoadCases()
    {
        var json = ReadEmbeddedResource("golden-cases.json");
        return JsonSerializer.Deserialize<List<GoldenCase>>(json, Options)
            ?? throw new InvalidOperationException("Failed to deserialize golden-cases.json.");
    }

    public static string LoadExpectedJson(string caseName) => ReadEmbeddedResource($"expected.{caseName}.json").TrimEnd('\r', '\n');

    public static PricingRequest BuildRequest(GoldenCase goldenCase, CatalogueSnapshot snapshot)
    {
        var market = snapshot.Markets.Single(m => m.Code == goldenCase.Market);
        return new PricingRequest(snapshot, goldenCase.Configuration, new PricingContext(market, goldenCase.SellerMultiplier));
    }

    private static string ReadEmbeddedResource(string suffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
