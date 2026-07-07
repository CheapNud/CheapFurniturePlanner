using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// ProductionIdentityService is a thin resolve-only bridge between a placement and the Domain
// ProductionIdentityResolver: it locates the owning model, pulls the modellenkamer's released
// variant names (VariantNamingService), and resolves the variant code against the always-published
// (Active) state - a placement is Released if its variant was named, else Composed.
public class ProductionIdentityServiceTests
{
    private sealed class FakeCatalogueSource(CatalogueSnapshot snapshot) : ICatalogueSource
    {
        public Task<CatalogueSnapshot> GetCurrentAsync() => Task.FromResult(snapshot);

        public void Invalidate() { }
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    // Mirrors VariantNamingServiceTests/ModelPublishServiceTests: an in-memory SQLite DB migrated to
    // the current schema, kept alive for the test's duration via the returned connection.
    private static (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (new TestDbContextFactory(options), connection);
    }

    // FJORD is never seeded into ModelStates here, so ModelPublishService.GetStateAsync defaults it
    // to Draft - which is exactly what VariantNamingService.AssignAsync requires to accept a naming.
    // That default-Draft gate is unrelated to the Active state ProductionIdentityService itself passes
    // to the resolver, so seeding a name this way does not contradict FJORD being released.
    private static VariantNamingService NewNaming(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source);
        return new VariantNamingService(factory, publish);
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
    public async Task ResolveForPlacementAsync_NoNamingRow_ReturnsComposedIdentity_WithCorrectVariantCode()
    {
        var snapshot = LoadFjordSnapshot();
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot), NewNaming(factory));
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

    // Task 3: once the modellenkamer has named a placement's variant, the released model's placements
    // must surface that assigned code as Released rather than the raw composed variant code.
    [Fact]
    public async Task ResolveForPlacementAsync_WithNamingRow_ReturnsReleasedIdentity_WithAssignedCode()
    {
        var snapshot = LoadFjordSnapshot();
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var naming = NewNaming(factory);
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot), naming);
        var placement = Fj2Placement();

        var composed = await service.ResolveForPlacementAsync(placement);
        Assert.NotNull(composed);
        await naming.AssignAsync("FJORD", composed!.VariantCode, "STUDIO-A");

        var identity = await service.ResolveForPlacementAsync(placement);

        Assert.NotNull(identity);
        Assert.Equal(ProductionCodeStatus.Released, identity!.Status);
        Assert.Equal("STUDIO-A", identity.EffectiveCode);
        Assert.True(identity.IsExportable);
    }

    [Fact]
    public async Task ResolveForPlacementAsync_MissingElementCode_ReturnsNull()
    {
        var snapshot = LoadFjordSnapshot();
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot), NewNaming(factory));
        var placement = new FurniturePlannerViewModel { ElementCode = null };

        var identity = await service.ResolveForPlacementAsync(placement);

        Assert.Null(identity);
    }

    [Fact]
    public async Task ResolveForPlacementAsync_UnknownElementCode_ReturnsNull()
    {
        var snapshot = LoadFjordSnapshot();
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new ProductionIdentityService(new FakeCatalogueSource(snapshot), NewNaming(factory));
        var placement = new FurniturePlannerViewModel { ElementCode = "DOES-NOT-EXIST" };

        var identity = await service.ResolveForPlacementAsync(placement);

        Assert.Null(identity);
    }
}
