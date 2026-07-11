using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Catalogue;

public class DbCatalogueSourceTests
{
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

    private static string SeedCurrentCatalogue(IDbContextFactory<FurniturePlannerContext> factory, string version, string modelCode)
    {
        var snapshot = new CatalogueSnapshot
        {
            Version = version,
            Models = [new FurnitureModel { Code = modelCode, Name = "Test Sofa" }],
        };
        var bundleJson = CanonicalJson.Serialize(snapshot);

        using var context = factory.CreateDbContext();
        context.PublishedCatalogues.Add(new PublishedCatalogue
        {
            Version = version,
            ContentHash = "irrelevant-hash",
            BundleJson = bundleJson,
            IsCurrent = true,
        });
        context.SaveChanges();
        return bundleJson;
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsSnapshot_FromCurrentPublishedRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            SeedCurrentCatalogue(factory, version: "2024.1", modelCode: "SOFA-001");
            var source = new DbCatalogueSource(factory);

            var snapshot = await source.GetCurrentAsync();

            Assert.Equal("2024.1", snapshot.Version);
            var model = Assert.Single(snapshot.Models);
            Assert.Equal("SOFA-001", model.Code);
        }
    }

    [Fact]
    public async Task GetCurrentAsync_CachesSnapshot_UntilInvalidated()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            SeedCurrentCatalogue(factory, version: "2024.1", modelCode: "SOFA-001");
            var source = new DbCatalogueSource(factory);

            var first = await source.GetCurrentAsync();
            var second = await source.GetCurrentAsync();
            Assert.Same(first, second);

            source.Invalidate();
            var third = await source.GetCurrentAsync();
            Assert.NotSame(first, third);
        }
    }

    [Fact]
    public async Task GetCurrentAsync_Throws_WhenNoCurrentCatalogue()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new DbCatalogueSource(factory);

            await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetCurrentAsync());
        }
    }

    [Fact]
    public async Task GetCurrentAsync_ScheduledVersion_WaitsUntilItsEffectiveDate()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var publishService = NewPublishService(factory);
        await publishService.PublishAsync(ValidSnapshotWithModel("A"), now);              // v1 effective now
        await publishService.PublishAsync(ValidSnapshotWithModel("B"), now.AddDays(10));  // v2 scheduled

        var source = new DbCatalogueSource(factory, () => now);
        var current = await source.GetCurrentAsync();
        Assert.Contains(current.Models, m => m.Code == "A");   // v1 served; v2 not yet effective

        now = now.AddDays(11);                                  // cross the boundary
        var afterBoundary = await source.GetCurrentAsync();     // re-resolves automatically (no Invalidate)
        Assert.Contains(afterBoundary.Models, m => m.Code == "B");
    }

    [Fact]
    public async Task GetCurrentAsync_NoEffectiveVersion_Throws()
    {
        var (factory, conn) = NewFactory(); using var _ = conn;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var publishService = NewPublishService(factory);
        await publishService.PublishAsync(ValidSnapshot(), now.AddDays(5));   // only a future version exists
        var source = new DbCatalogueSource(factory, () => now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetCurrentAsync());
    }

    private static CataloguePublishService NewPublishService(IDbContextFactory<FurniturePlannerContext> factory) =>
        new(factory, new NoOpCatalogueSource());

    private static CatalogueSnapshot ValidSnapshot() => ValidSnapshotWithModel("M1");

    private static CatalogueSnapshot ValidSnapshotWithModel(string modelCode) => new()
    {
        Version = "irrelevant",
        Models = [new FurnitureModel { Code = modelCode, Name = $"Model {modelCode}", Elements = [new Element { Code = "E1", Name = "Element One" }] }],
    };

    private sealed class NoOpCatalogueSource : ICatalogueSource
    {
        public Task<CatalogueSnapshot> GetCurrentAsync() => throw new NotSupportedException();
        public void Invalidate() { }
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
