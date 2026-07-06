using System.Text.Json;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Catalogue;

// Cross-boundary determinism proof for Phase 2A: the embedded Fjord seed round-trips through a
// real SQLite-backed publish/read cycle (CataloguePublishService -> PublishedCatalogue row ->
// DbCatalogueSource) and still prices byte-identically to the Domain layer's hand-verified
// golden masters. The leather case additionally proves the material-type BOM-significance fix
// (Phase 2A Task 1) survives a full persistence round trip, not just an in-memory snapshot.
public class CatalogueEndToEndTests
{
    // Mirrors CheapFurniturePlanner.Domain.Tests.Golden.GoldenCaseLoader's GoldenCase shape so the
    // exact same golden-cases.json manifest can be deserialized directly into domain types here.
    private sealed record GoldenCase(string Name, string Market, decimal SellerMultiplier, ProductConfiguration Configuration);

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
    public async Task PublishedFjordSnapshot_RoundTripsThroughSqlite_AndPricesByteIdenticalToDomainGoldens()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new DbCatalogueSource(factory);
            var publishService = new CataloguePublishService(factory, source);

            var seedSnapshot = LoadEmbeddedFjordSeed();
            var publishResult = await publishService.PublishAsync(seedSnapshot);
            Assert.True(publishResult.Success, $"Publish failed: {string.Join("; ", publishResult.Errors)}");

            var roundTrippedSnapshot = await source.GetCurrentAsync();

            // Plain fabric configuration - the same scenario covered by the Domain golden
            // "std-plain-aqua-euw".
            AssertPricesLikeDomainGolden(roundTrippedSnapshot, "std-plain-aqua-euw");

            // Leather configuration (thick hide, secondary fabric group) - covered by the Domain
            // golden "fjch-thick-leather" added for the material-type engine fix.
            AssertPricesLikeDomainGolden(roundTrippedSnapshot, "fjch-thick-leather");
        }
    }

    private static void AssertPricesLikeDomainGolden(CatalogueSnapshot snapshot, string caseName)
    {
        var goldenCase = LoadGoldenCase(caseName);
        var market = snapshot.Markets.Single(m => m.Code == goldenCase.Market);
        var request = new PricingRequest(snapshot, goldenCase.Configuration, new PricingContext(market, goldenCase.SellerMultiplier));

        var result = PricingEngine.Calculate(request);

        Assert.True(result.IsSuccess, $"[{caseName}] Expected success but got errors: {string.Join(", ", result.Errors.Select(e => $"{e.Kind}:{e.Subject}"))}");
        var actualBreakdown = result.Breakdown!;
        var actualJson = CanonicalJson.Serialize(actualBreakdown);

        // CataloguePublishService intentionally stamps every publish with its own sequential
        // Version/ContentHash (see CataloguePublishService.PublishAsync), independent of whatever
        // "Version" the source file declares. The Domain golden was captured against the raw fixture
        // (DemoWorld.Load(), which keeps the file's own "fjord-1.0.0" version), so CatalogueVersion
        // and ContentHash are expected to differ here and are reconciled before comparing - every
        // other field (all costs, quantities, stage subtotals, markup trace, totals) must still
        // match byte-for-byte to prove the SQLite round trip didn't change any priced value.
        var expectedBreakdown = CanonicalJson.Deserialize<PriceBreakdown>(LoadDomainGoldenExpectedJson(caseName))!;
        var reconciledExpectedJson = CanonicalJson.Serialize(expectedBreakdown with
        {
            CatalogueVersion = actualBreakdown.CatalogueVersion,
            ContentHash = actualBreakdown.ContentHash,
        });

        Assert.Equal(reconciledExpectedJson, actualJson);
    }

    private static CatalogueSnapshot LoadEmbeddedFjordSeed()
    {
        var asm = typeof(CataloguePublishService).Assembly;
        using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json")
            ?? throw new InvalidOperationException("Embedded resource 'CheapFurniturePlanner.Seed.demo-catalogue.json' not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return CanonicalJson.Deserialize<CatalogueSnapshot>(json)
            ?? throw new InvalidOperationException("Failed to deserialize embedded demo-catalogue.json.");
    }

    // The Domain.Tests golden fixtures (golden-cases.json + expected/*.json) are test assets, not
    // shipping code, so they're read here by repo-relative path rather than duplicated as embedded
    // resources in this project. This keeps the two suites provably pricing the same scenarios
    // instead of two copies that could silently drift apart.
    private static GoldenCase LoadGoldenCase(string caseName)
    {
        var path = Path.Combine(FindRepoRoot(), "CheapFurniturePlanner.Domain.Tests", "Golden", "golden-cases.json");
        var json = File.ReadAllText(path);
        var cases = JsonSerializer.Deserialize<List<GoldenCase>>(json)
            ?? throw new InvalidOperationException("Failed to deserialize golden-cases.json.");
        return cases.Single(c => c.Name == caseName);
    }

    private static string LoadDomainGoldenExpectedJson(string caseName)
    {
        var path = Path.Combine(FindRepoRoot(), "CheapFurniturePlanner.Domain.Tests", "Golden", "expected", $"{caseName}.json");
        return File.ReadAllText(path).TrimEnd('\r', '\n');
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CheapFurniturePlanner.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (CheapFurniturePlanner.sln) above the test assembly directory.");
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
