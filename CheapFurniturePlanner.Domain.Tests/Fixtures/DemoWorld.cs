using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Domain.Tests.Fixtures;

// Loads the embedded "Fjord" demo catalogue used by golden-master/determinism/error-coverage tests.
// Deserializes with the same converter set CanonicalJson uses (JsonStringEnumConverter) so enum
// values like RoundStage flags and MaterialKind round-trip identically, then stamps ContentHash.
public static class DemoWorld
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static CatalogueSnapshot Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("demo-catalogue.json", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var snapshot = JsonSerializer.Deserialize<CatalogueSnapshot>(json, Options)
            ?? throw new InvalidOperationException("Failed to deserialize demo-catalogue.json.");

        snapshot.ContentHash = snapshot.ComputeContentHash();
        return snapshot;
    }
}
