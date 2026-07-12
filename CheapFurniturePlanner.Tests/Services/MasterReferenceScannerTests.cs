using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 2: MasterReferenceScanner reports where a master is referenced (BOM lines, substitutions,
// sibling masters) so the authoring service can block deletion. Pure function over a hand-built
// snapshot (invented codes) — no seed dependency. The guarded-delete tests drive the service.
public class MasterReferenceScannerTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    // A minimal catalogue with invented codes: model M-A / element E-A whose BOM references MAT-A
    // (misc), OP-A (labor), FRAME-A (frame); a substitution swaps MAT-A -> MAT-B; a spray price and a
    // fabric group reference FRAME-A and PG-A respectively. MAT-FREE / OP-FREE / FRAME-FREE / PG-FREE
    // are unreferenced.
    private static CatalogueSnapshot BuildSnapshot()
    {
        var element = new Element
        {
            Code = "E-A",
            Name = "Element A",
            Options = [],
            Bom = new BomDocument
            {
                Sections =
                [
                    new BomSection { Kind = BomSectionKind.Misc, Lines = [new MiscBomLine { LineKey = "L1", MaterialCode = "MAT-A" }] },
                    new BomSection { Kind = BomSectionKind.Labor, Lines = [new LaborBomLine { LineKey = "L2", OperationCode = "OP-A" }] },
                    new BomSection { Kind = BomSectionKind.Frame, Lines = [new FrameBomLine { LineKey = "L3", FrameBodyCode = "FRAME-A" }] },
                ]
            },
            Substitutions = [new SubstitutionRule(new ApplicabilityCondition([]), "MAT-A", "MAT-B", null)],
        };
        return new CatalogueSnapshot
        {
            Version = "",
            Models = [new FurnitureModel { Code = "M-A", Name = "Model A", Elements = [element] }],
            Materials = [new Material("MAT-A", "A", 1m, "m"), new Material("MAT-B", "B", 1m, "m"), new Material("MAT-FREE", "free", 1m, "m")],
            Operations = [new Operation("OP-A", "A", 1m), new Operation("OP-FREE", "free", 1m)],
            FrameBodies = [new FrameBody("FRAME-A", 1m, 1m, 0m, 0m), new FrameBody("FRAME-FREE", 1m, 1m, 0m, 0m)],
            SprayPrices = [new SprayPrice("FRAME-A", 2m)],
            PriceGroups = [new PriceGroup { Code = "PG-A", Kind = MaterialKind.Fabric, RatePerMeter = 1m }, new PriceGroup { Code = "PG-FREE", Kind = MaterialKind.Fabric, RatePerMeter = 1m }],
            FabricGroups = [new FabricGroup { Code = "FG-A", PriceGroupCode = "PG-A" }],
        };
    }

    [Theory]
    [InlineData(MasterKind.Material, "MAT-A")]     // misc line + substitution
    [InlineData(MasterKind.Operation, "OP-A")]     // labor line
    [InlineData(MasterKind.FrameBody, "FRAME-A")]  // frame line + spray price
    [InlineData(MasterKind.PriceGroup, "PG-A")]    // fabric group
    public void FindReferences_ReferencedMaster_ReturnsNonEmpty(MasterKind kind, string code)
    {
        Assert.NotEmpty(MasterReferenceScanner.FindReferences(BuildSnapshot(), kind, code));
    }

    [Fact]
    public void FindReferences_MaterialUsedByLineAndSubstitution_ReportsBoth()
    {
        var refs = MasterReferenceScanner.FindReferences(BuildSnapshot(), MasterKind.Material, "MAT-A");
        Assert.True(refs.Count >= 2);
    }

    [Theory]
    [InlineData(MasterKind.Material, "MAT-FREE")]
    [InlineData(MasterKind.Operation, "OP-FREE")]
    [InlineData(MasterKind.FrameBody, "FRAME-FREE")]
    [InlineData(MasterKind.PriceGroup, "PG-FREE")]
    [InlineData(MasterKind.SprayPrice, "FRAME-A")]      // nothing references a spray price
    [InlineData(MasterKind.FixedSurcharge, "anything")] // never scanned
    [InlineData(MasterKind.ChoiceSurcharge, "anything")]
    public void FindReferences_Unreferenced_ReturnsEmpty(MasterKind kind, string code)
    {
        Assert.Empty(MasterReferenceScanner.FindReferences(BuildSnapshot(), kind, code));
    }

    // --- guard is enforced through the service against a seeded store ---
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

    [Fact]
    public async Task DeleteMaterial_Referenced_ThrowsMasterReferenced()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(BuildSnapshot());
        var service = new MasterAuthoringService(store);

        await Assert.ThrowsAsync<MasterReferencedException>(() => service.DeleteMaterialAsync("MAT-A"));
        Assert.Contains((await store.LoadAsync()).Materials, m => m.Code == "MAT-A");
    }

    [Fact]
    public async Task DeleteFrameBody_WithSprayPrice_ThrowsMasterReferenced()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(BuildSnapshot());
        var service = new MasterAuthoringService(store);

        await Assert.ThrowsAsync<MasterReferencedException>(() => service.DeleteFrameBodyAsync("FRAME-A"));
    }

    [Fact]
    public async Task DeleteMaterial_Unreferenced_Succeeds()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(BuildSnapshot());
        var service = new MasterAuthoringService(store);

        await service.DeleteMaterialAsync("MAT-FREE");
        Assert.DoesNotContain((await store.LoadAsync()).Materials, m => m.Code == "MAT-FREE");
    }

    [Fact]
    public async Task DeleteSprayPrice_AlwaysSucceeds()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(BuildSnapshot());
        var service = new MasterAuthoringService(store);

        await service.DeleteSprayPriceAsync("FRAME-A");
        Assert.Empty((await store.LoadAsync()).SprayPrices);
    }
}
