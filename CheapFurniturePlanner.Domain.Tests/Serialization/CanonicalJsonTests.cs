using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Serialization;

public class CanonicalJsonTests
{
    private static CatalogueSnapshot CreateSnapshot(decimal ratePerMeter = 12.5m) => new()
    {
        Version = "1.0.0",
        PriceGroups =
        [
            new PriceGroup { Id = 1, Code = "PG-1", Kind = MaterialKind.Fabric, RatePerMeter = ratePerMeter }
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
}
