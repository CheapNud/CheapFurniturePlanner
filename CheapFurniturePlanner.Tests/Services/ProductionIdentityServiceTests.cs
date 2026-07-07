using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.ViewModels;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// ProductionIdentityService is now a thin resolve-only bridge between a placement and the Domain
// ProductionIdentityResolver: it locates the owning model and resolves the composed variant code
// against the always-published (Active) state - there is no registry to write to and no suggestion
// set to apply, since the modellenkamer is now the sole gatekeeper for what reaches the catalogue.
public class ProductionIdentityServiceTests
{
    private sealed class FakeCatalogueSource(CatalogueSnapshot snapshot) : ICatalogueSource
    {
        public Task<CatalogueSnapshot> GetCurrentAsync() => Task.FromResult(snapshot);

        public void Invalidate() { }
    }

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

    private static FurniturePlannerViewModel Fj2Placement() => new()
    {
        ElementCode = "FJ2",
        Selections = new Dictionary<string, string> { ["DEPTH"] = "STD", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" },
        FabricColorCode = "AQUA-BLUE",
    };

    [Fact]
    public async Task ResolveForPlacementAsync_ReturnsComposedIdentity_WithCorrectVariantCode()
    {
        var snapshot = LoadFjordSnapshot();
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot));
        var placement = Fj2Placement();

        var identity = await service.ResolveForPlacementAsync(placement);

        Assert.NotNull(identity);
        Assert.Equal(ProductionCodeStatus.Composed, identity!.Status);
        Assert.Equal(identity.VariantCode, identity.EffectiveCode);
        Assert.True(identity.IsExportable);

        var expectedConfig = new ProductConfiguration("FJORD",
            [new ElementSelection("FJ2", 1, placement.Selections, placement.FabricColorCode)]);
        var expected = ProductionIdentityResolver.Resolve(snapshot, expectedConfig, new Dictionary<string, string>(), TradeItemState.Active)[0];
        Assert.Equal(expected.VariantCode, identity.VariantCode);
    }

    [Fact]
    public async Task ResolveForPlacementAsync_MissingElementCode_ReturnsNull()
    {
        var snapshot = LoadFjordSnapshot();
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot));
        var placement = new FurniturePlannerViewModel { ElementCode = null };

        var identity = await service.ResolveForPlacementAsync(placement);

        Assert.Null(identity);
    }

    [Fact]
    public async Task ResolveForPlacementAsync_UnknownElementCode_ReturnsNull()
    {
        var snapshot = LoadFjordSnapshot();
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot));
        var placement = new FurniturePlannerViewModel { ElementCode = "DOES-NOT-EXIST" };

        var identity = await service.ResolveForPlacementAsync(placement);

        Assert.Null(identity);
    }
}
