using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.ViewModels;
using Xunit;

namespace CheapFurniturePlanner.Tests.ViewModels;

// Proves the Add-Furniture dialog's data source: every element of every model in the snapshot
// must show up as one flat, addable option carrying the owning model's identity plus the
// element's own dimensions - the shape the dialog groups by ModelName and prices from once added.
public class CatalogueElementOptionTests
{
    private static CatalogueSnapshot LoadFjordSnapshot()
    {
        var asm = typeof(CataloguePublishService).Assembly;
        using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json")
            ?? throw new InvalidOperationException("Embedded resource 'CheapFurniturePlanner.Seed.demo-catalogue.json' not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return CanonicalJson.Deserialize<CatalogueSnapshot>(json)
            ?? throw new InvalidOperationException("Failed to deserialize embedded demo-catalogue.json.");
    }

    [Fact]
    public void FromSnapshot_ProjectsEveryElementOfEveryModel_WithModelIdentityAndOwnDimensions()
    {
        var snapshot = LoadFjordSnapshot();

        var options = CatalogueElementOption.FromSnapshot(snapshot);

        var expectedCount = snapshot.Models.Sum(m => m.Elements.Count);
        Assert.Equal(expectedCount, options.Count);

        var fj2 = Assert.Single(options, o => o.ElementCode == "FJ2");
        Assert.Equal("FJORD", fj2.ModelCode);
        Assert.Equal("Fjord", fj2.ModelName);
        Assert.Equal("Fjord 2-Seat", fj2.ElementName);
        Assert.Equal(180.0, fj2.Width);
        Assert.Equal(95.0, fj2.Depth);
        Assert.Equal(80.0, fj2.Height);
    }

    [Fact]
    public void FromSnapshot_NoModels_ReturnsEmptyList()
    {
        var snapshot = new CatalogueSnapshot { Version = "1" };

        var options = CatalogueElementOption.FromSnapshot(snapshot);

        Assert.Empty(options);
    }
}
