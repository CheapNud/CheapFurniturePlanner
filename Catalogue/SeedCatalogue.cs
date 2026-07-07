using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;

namespace CheapFurniturePlanner.Catalogue;

public static class SeedCatalogue
{
    // The embedded Fjord seed IS the authoring catalogue in this slice (structure authoring is a later phase).
    public static CatalogueSnapshot Load()
    {
        var asm = typeof(SeedCatalogue).Assembly;
        using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json")
            ?? throw new InvalidOperationException("Embedded Fjord seed catalogue resource not found.");
        using var reader = new StreamReader(stream);
        return CanonicalJson.Deserialize<CatalogueSnapshot>(reader.ReadToEnd())
            ?? throw new InvalidOperationException("Failed to deserialize the embedded Fjord seed catalogue.");
    }
}
