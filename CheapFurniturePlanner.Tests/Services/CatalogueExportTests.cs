using System.Text;
using System.Text.RegularExpressions;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Harness mirrors PartyServiceTests: in-memory SQLite, migrated schema. The published catalogue
// is seeded from the same embedded "Fjord" demo bundle CatalogueEndToEndTests round-trips - a
// real, fully priceable snapshot, so GenerateCsvAsync exercises the real flattener/pricing path
// instead of a hand-rolled stub.
public class CatalogueExportTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

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

    private static string NewOutputRoot() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private static CatalogueSnapshot LoadEmbeddedFjordSeed()
    {
        var asm = typeof(CataloguePublishService).Assembly;
        using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json")
            ?? throw new InvalidOperationException("Embedded resource 'CheapFurniturePlanner.Seed.demo-catalogue.json' not found.");
        using var reader = new StreamReader(stream);
        return CanonicalJson.Deserialize<CatalogueSnapshot>(reader.ReadToEnd())
            ?? throw new InvalidOperationException("Failed to deserialize embedded demo-catalogue.json.");
    }

    // Stamps the given version + a real ComputeContentHash and inserts the row directly - the
    // same shape DbCatalogueSourceTests.SeedCurrentCatalogue uses, without going through
    // CataloguePublishService (which assigns its own sequential version).
    private static string SeedPublishedCatalogue(IDbContextFactory<FurniturePlannerContext> factory, string version)
    {
        var snapshot = LoadEmbeddedFjordSeed();
        snapshot.Version = version;
        snapshot.ContentHash = snapshot.ComputeContentHash();
        var bundleJson = CanonicalJson.Serialize(snapshot);

        using var db = factory.CreateDbContext();
        db.PublishedCatalogues.Add(new PublishedCatalogue
        {
            Version = version,
            ContentHash = snapshot.ContentHash,
            BundleJson = bundleJson,
            IsCurrent = true,
        });
        db.SaveChanges();
        return bundleJson;
    }

    [Fact]
    public async Task GenerateCsv_WritesSemicolonInvariantFile()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "7");
        var export = new CatalogueExport(factory, NewOutputRoot());

        var filePath = await export.GenerateCsvAsync("7");

        Assert.True(File.Exists(filePath));
        var lines = (await File.ReadAllLinesAsync(filePath)).Where(l => l.Length > 0).ToArray();
        Assert.Equal("CatalogueVersion;ContentHash;ModelCode;ModelName;CollectionCode;ElementCode;ElementName;PriceGroupCode;MarketCode;Price",
            lines[0]);

        var dataLines = lines.Skip(1).ToArray();
        Assert.NotEmpty(dataLines);
        var commaDecimal = new Regex(@"^\d+,\d\d$");
        var dotDecimal = new Regex(@"^\d+\.\d\d$");
        var sawPrice = false;
        foreach (var line in dataLines)
        {
            var fields = line.Split(';');
            Assert.Equal(10, fields.Length);
            Assert.StartsWith("7;", line);
            Assert.All(fields, f => Assert.DoesNotMatch(commaDecimal, f));
            if (dotDecimal.IsMatch(fields[9])) { sawPrice = true; }
        }
        Assert.True(sawPrice, "expected at least one field matching the invariant price format 0.00");

        var expectedHash = (await (await factory.CreateDbContextAsync()).PublishedCatalogues.AsNoTracking()
            .FirstAsync(c => c.Version == "7")).ContentHash;
        Assert.All(dataLines, line => Assert.Equal(expectedHash, line.Split(';')[1]));
    }

    [Fact]
    public async Task GenerateJson_IsByteIdenticalToStoredBundle()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var bundleJson = SeedPublishedCatalogue(factory, "7");
        var export = new CatalogueExport(factory, NewOutputRoot());

        var filePath = await export.GenerateJsonAsync("7");

        var writtenBytes = await File.ReadAllBytesAsync(filePath);
        Assert.Equal(Encoding.UTF8.GetBytes(bundleJson), writtenBytes);
    }

    // Export 1: GenerateCsvAsync formats every price with "0.00" - that format string always prints
    // exactly 2 decimals, it does not itself round. The engine value only actually carries 2
    // decimals because RoundStage.Final ran during pricing (FinalizeStages.RoundFinal). Pins that
    // every market in the fixture the CSV tests round-trip actually enables RoundStage.Final, so the
    // "0.00" format cannot silently diverge from the priced value today - see the comment on
    // CatalogueExport.GenerateCsvAsync for what to do if a market ever needs to omit it.
    [Fact]
    public void FixtureMarkets_AllEnableRoundStageFinal()
    {
        var snapshot = LoadEmbeddedFjordSeed();

        Assert.NotEmpty(snapshot.Markets);
        Assert.All(snapshot.Markets, market => Assert.True(market.Rounding.Stages.HasFlag(RoundStage.Final),
            $"Market '{market.Code}' does not enable RoundStage.Final - the CSV's \"0.00\" format would print an unrounded price."));
    }

    [Fact]
    public async Task Generate_UnknownVersionThrows()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var export = new CatalogueExport(factory, NewOutputRoot());

        var csvEx = await Assert.ThrowsAsync<InvalidOperationException>(() => export.GenerateCsvAsync("missing"));
        Assert.Contains("missing", csvEx.Message);

        var jsonEx = await Assert.ThrowsAsync<InvalidOperationException>(() => export.GenerateJsonAsync("missing"));
        Assert.Contains("missing", jsonEx.Message);
    }
}
