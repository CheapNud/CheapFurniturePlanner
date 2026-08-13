using System.Text.Json;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using CheapFurniturePlanner.Domain.Tests.Golden;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Serialization;

public class CanonicalJsonTests
{
    private static CatalogueSnapshot CreateSnapshot(decimal ratePerMeter = 12.5m) => new()
    {
        Version = "1.0.0",
        PriceGroups =
        [
            new PriceGroup { Id = 1, Code = "PG-1", Kind = FabricMaterialKind.Fabric, RatePerMeter = ratePerMeter }
        ]
    };

    [Fact]
    public void Serialize_CalledTwiceOnSameSnapshot_ProducesIdenticalStrings()
    {
        // Arrange
        var snapshot = CreateSnapshot();

        // Act
        var first = CanonicalJson.Serialize(snapshot);
        var second = CanonicalJson.Serialize(snapshot);

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Sha256Hex_CalledTwiceOnSameSnapshot_IsStable()
    {
        // Arrange
        var snapshot = CreateSnapshot();

        // Act
        var first = CanonicalJson.Sha256Hex(snapshot);
        var second = CanonicalJson.Sha256Hex(snapshot);

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeContentHash_ChangesWhenPriceGroupRatePerMeterChanges()
    {
        // Arrange
        var original = CreateSnapshot(ratePerMeter: 12.5m);
        var modified = CreateSnapshot(ratePerMeter: 99.9m);

        // Act
        var originalHash = original.ComputeContentHash();
        var modifiedHash = modified.ComputeContentHash();

        // Assert
        Assert.NotEqual(originalHash, modifiedHash);
    }

    [Fact]
    public void ComputeContentHash_IsNotInfluencedByContentHashFieldValue()
    {
        // Arrange
        var snapshot = CreateSnapshot();

        // Act
        snapshot.ContentHash = "some-stale-hash-value";
        var hashA = snapshot.ComputeContentHash();

        snapshot.ContentHash = "a-completely-different-value";
        var hashB = snapshot.ComputeContentHash();

        // Assert
        Assert.Equal(hashA, hashB);
    }

    // Item 5: pins the golden-hash foundation itself. Property order is never asserted by the
    // GoldenMasterTests string-equality check reordering all properties consistently would still
    // produce a "byte-identical" (to itself) but WRONG golden - this test guards the record
    // declaration order that CanonicalJson (default System.Text.Json, no [JsonPropertyOrder]) relies
    // on, against a future field reorder or attribute slipping in unnoticed.
    [Fact]
    public void Serialize_GoldenBreakdown_PreservesDeclaredPropertyOrder()
    {
        // Arrange - a real pricing run through the full engine, not a hand-built record, so this
        // pins the actual shape that reaches the golden hash.
        var snapshot = DemoWorld.Load();
        var goldenCase = GoldenCaseLoader.LoadCases().Single(c => c.Name == "std-plain-aqua-euw");
        var request = GoldenCaseLoader.BuildRequest(goldenCase, snapshot);
        var result = PricingEngine.Calculate(request);
        Assert.True(result.IsSuccess, $"Expected success but got errors: {string.Join(", ", result.Errors.Select(e => $"{e.Kind}:{e.Subject}"))}");

        // Act
        var json = CanonicalJson.Serialize(result.Breakdown);
        using var document = JsonDocument.Parse(json);

        // Assert - top-level PriceBreakdown key sequence.
        Assert.Equal(
            ["CatalogueVersion", "ContentHash", "MarketCode", "Elements", "DocumentTotal"],
            document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());

        // Nested ElementBreakdown key sequence.
        var element = document.RootElement.GetProperty("Elements")[0];
        Assert.Equal(
            ["ElementCode", "VariantCode", "Quantity", "Lines", "StageSubtotals", "MarkupTrace", "ElementTotal"],
            element.EnumerateObject().Select(p => p.Name).ToArray());

        // Nested BreakdownLine key sequence.
        var line = element.GetProperty("Lines")[0];
        Assert.Equal(
            ["Stage", "Category", "Description", "SourceLineKey", "Quantity", "Unit", "UnitCost", "LineTotal"],
            line.EnumerateObject().Select(p => p.Name).ToArray());

        // Nested MarkupTraceEntry key sequence.
        var markupTrace = element.GetProperty("MarkupTrace")[0];
        Assert.Equal(
            ["StepName", "Percent", "Mode", "ResultAfter"],
            markupTrace.EnumerateObject().Select(p => p.Name).ToArray());
    }
}
