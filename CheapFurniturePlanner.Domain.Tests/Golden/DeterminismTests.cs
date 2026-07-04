using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Golden;

// Every golden case must be fully deterministic: running it twice must yield byte-identical
// serialization and stable hashes, and the snapshot's own content hash must not drift between loads.
public class DeterminismTests
{
    public static IEnumerable<object[]> Cases() => GoldenCaseLoader.LoadCases().Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Calculate_RunTwice_ProducesIdenticalSerializationAndHash(string caseName)
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var goldenCase = GoldenCaseLoader.LoadCases().Single(c => c.Name == caseName);
        var request = GoldenCaseLoader.BuildRequest(goldenCase, snapshot);

        // Act
        var first = PricingEngine.Calculate(request);
        var second = PricingEngine.Calculate(request);

        // Assert
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        var firstJson = CanonicalJson.Serialize(first.Breakdown);
        var secondJson = CanonicalJson.Serialize(second.Breakdown);
        Assert.Equal(firstJson, secondJson);

        var firstHash = CanonicalJson.Sha256Hex(first.Breakdown);
        var secondHash = CanonicalJson.Sha256Hex(second.Breakdown);
        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public void Load_CalledTwice_ProducesStableContentHash()
    {
        // Act
        var first = DemoWorld.Load();
        var second = DemoWorld.Load();

        // Assert
        Assert.NotEmpty(first.ContentHash);
        Assert.Equal(first.ContentHash, second.ContentHash);
    }
}
