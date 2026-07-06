using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Configurator;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.ViewModels;
using Xunit;

namespace CheapFurniturePlanner.Tests.Configurator;

// PlannerFurnitureItem persists no Name/dimensions for element placements (only ElementCode +
// configuration), so RoomPlanService's Mapster map always yields Code/Name="" and
// FurnitureWidth/Length/Height=null for a freshly-loaded placement. Left unhydrated, the planner
// canvas sizes the item via FurnitureWidth.GetValueOrDefault() and renders it as an invisible 0x0
// box. These tests exercise the resolver directly against the real embedded Fjord seed (the same
// fixture ConfigurationResolverTests/CatalogueEndToEndTests use).
public class PlannerElementHydratorTests
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
    public void Hydrate_KnownElementCode_FillsNameAndDimensions()
    {
        var snapshot = LoadFjordSnapshot();
        // Mirrors what RoomPlanService actually returns for a loaded element placement: ElementCode +
        // config restored, but Code/Name empty and dimensions null (nothing populates those on load).
        var placement = new FurniturePlannerViewModel
        {
            ElementCode = "FJ2",
            Code = "",
            Name = "",
            FurnitureWidth = null,
            FurnitureLength = null,
            FurnitureHeight = null,
        };

        PlannerElementHydrator.Hydrate(placement, snapshot);

        Assert.Equal("FJ2", placement.Code);
        Assert.Equal("Fjord 2-Seat", placement.Name);
        Assert.Equal(180.0, placement.FurnitureWidth);
        Assert.Equal(95.0, placement.FurnitureLength);
        Assert.Equal(80.0, placement.FurnitureHeight);
    }

    [Fact]
    public void Hydrate_NoElementCode_LeavesPlacementUntouched()
    {
        var snapshot = LoadFjordSnapshot();
        var placement = new FurniturePlannerViewModel { ElementCode = null, Code = "SOFA-1", Name = "Classic Sofa" };

        PlannerElementHydrator.Hydrate(placement, snapshot);

        // Legacy flat-catalog placements (no ElementCode) already carry their own Name/dimensions via
        // the FurnitureItem mapping - hydration must not touch them.
        Assert.Equal("SOFA-1", placement.Code);
        Assert.Equal("Classic Sofa", placement.Name);
    }

    [Fact]
    public void Hydrate_DanglingElementCode_LeavesPlacementUntouched()
    {
        var snapshot = LoadFjordSnapshot();
        var placement = new FurniturePlannerViewModel { ElementCode = "DOES-NOT-EXIST", Code = "", Name = "" };

        PlannerElementHydrator.Hydrate(placement, snapshot);

        // A code that no longer exists in the catalogue is left as-is; the config panel already
        // surfaces this as "unavailable" via the pricing error path.
        Assert.Equal("", placement.Code);
        Assert.Equal("", placement.Name);
        Assert.Null(placement.FurnitureWidth);
    }
}
