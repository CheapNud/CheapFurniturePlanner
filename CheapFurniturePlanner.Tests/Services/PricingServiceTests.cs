using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.ViewModels;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Proves PricingService is a thin, correct bridge between a placement (FurniturePlannerViewModel)
// and the Domain PricingEngine: it must resolve the owning model from the element code, build the
// ProductConfiguration the engine expects, and price it against the first market (market selection
// is out of scope until a later phase). The expected total is the same Domain golden
// ("std-plain-aqua-euw") CatalogueEndToEndTests already proves prices identically end to end, so a
// mismatch here means the bridge - not the engine - is wrong.
public class PricingServiceTests
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

    [Fact]
    public async Task PriceAsync_PricesStdPlainAquaEuwGoldenPlacement_MatchesGoldenDocumentTotal()
    {
        var snapshot = LoadFjordSnapshot();
        var service = new PricingService(new FakeCatalogueSource(snapshot));
        var placement = new FurniturePlannerViewModel
        {
            ElementCode = "FJ2",
            Selections = new Dictionary<string, string> { ["DEPTH"] = "STD", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" },
            FabricColorCode = "AQUA-BLUE",
        };

        var result = await service.PriceAsync(placement);

        Assert.True(result.IsSuccess, $"Expected success but got errors: {string.Join(", ", result.Errors.Select(e => $"{e.Kind}:{e.Subject}"))}");
        Assert.Equal(407m, result.Breakdown!.DocumentTotal);
    }

    [Fact]
    public async Task PriceAsync_ReturnsUnknownElementError_ForElementCodeNotInAnyModel()
    {
        var snapshot = LoadFjordSnapshot();
        var service = new PricingService(new FakeCatalogueSource(snapshot));
        var placement = new FurniturePlannerViewModel { ElementCode = "DOES-NOT-EXIST" };

        var result = await service.PriceAsync(placement);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Kind == PricingErrorKind.UnknownElement);
    }

    [Fact]
    public async Task PriceAsync_ReturnsUnknownElementError_WhenElementCodeIsMissing()
    {
        var snapshot = LoadFjordSnapshot();
        var service = new PricingService(new FakeCatalogueSource(snapshot));
        var placement = new FurniturePlannerViewModel { ElementCode = null };

        var result = await service.PriceAsync(placement);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Kind == PricingErrorKind.UnknownElement);
    }
}
