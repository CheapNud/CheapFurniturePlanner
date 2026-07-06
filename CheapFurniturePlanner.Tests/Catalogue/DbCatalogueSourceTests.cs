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

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
