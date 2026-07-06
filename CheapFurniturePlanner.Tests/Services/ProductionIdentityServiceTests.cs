using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.ViewModels;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// ProductionIdentityService is the thin app-side bridge between a placement and the Domain
// ProductionIdentityResolver: it must resolve the owning model, register the variant idempotently
// (so it shows up in the modellenkamer/assignment workflow), then resolve again with the current
// suggestions + model state so the panel can display the effective code and its status.
public class ProductionIdentityServiceTests
{
    // Mirrors CodeAssignmentServiceTests.NewContext(): the connection is not owned by the context, so
    // callers must dispose it themselves to keep the in-memory database alive for the test's duration.
    private static (FurniturePlannerContext Context, Microsoft.Data.Sqlite.SqliteConnection Connection) NewContext()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        var ctx = new FurniturePlannerContext(options);
        ctx.Database.Migrate();
        return (ctx, conn);
    }

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
    public async Task ResolveForPlacementAsync_NoSuggestionYet_ReturnsComposedWithCorrectVariantCode_AndRegistersTemplateRow()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var snapshot = LoadFjordSnapshot();
        var assignments = new CodeAssignmentService(ctx);
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot), assignments);
        var placement = Fj2Placement();

        var identity = await service.ResolveForPlacementAsync(placement);

        Assert.NotNull(identity);
        Assert.Equal(ProductionCodeStatus.Composed, identity!.Status);
        Assert.Equal(identity.VariantCode, identity.EffectiveCode);
        Assert.False(identity.IsExportable);

        var expectedConfig = new ProductConfiguration("FJORD",
            [new ElementSelection("FJ2", 1, placement.Selections, placement.FabricColorCode)]);
        var expected = ProductionIdentityResolver.Resolve(snapshot, expectedConfig, new Dictionary<string, string>(), Domain.Catalog.TradeItemState.Draft)[0];
        Assert.Equal(expected.VariantCode, identity.VariantCode);

        var templates = await assignments.GetForModelAsync("FJORD");
        Assert.Contains(templates, t => t.VariantCode == identity.VariantCode);
    }

    [Fact]
    public async Task ResolveForPlacementAsync_AfterAssignAndRelease_ReturnsReleasedWithAssignedCode()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var snapshot = LoadFjordSnapshot();
        var assignments = new CodeAssignmentService(ctx);
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot), assignments);
        var placement = Fj2Placement();

        var seed = await service.ResolveForPlacementAsync(placement);
        Assert.NotNull(seed);
        var template = (await assignments.GetForModelAsync("FJORD")).Single(t => t.VariantCode == seed!.VariantCode);
        await assignments.AssignAsync(template.Id, "18E", null);
        await assignments.ReleaseModelAsync("FJORD");

        var identity = await service.ResolveForPlacementAsync(placement);

        Assert.NotNull(identity);
        Assert.Equal(ProductionCodeStatus.Released, identity!.Status);
        Assert.Equal("18E", identity.EffectiveCode);
        Assert.True(identity.IsExportable);
    }

    [Fact]
    public async Task ResolveForPlacementAsync_MissingElementCode_ReturnsNull()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var snapshot = LoadFjordSnapshot();
        var assignments = new CodeAssignmentService(ctx);
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot), assignments);
        var placement = new FurniturePlannerViewModel { ElementCode = null };

        var identity = await service.ResolveForPlacementAsync(placement);

        Assert.Null(identity);
    }
}
