using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Fixtures;

public class DemoWorldTests
{
    [Fact]
    public void Load_ReturnsFjordSnapshotWithThreeElementsAndNonEmptyHash()
    {
        // Act
        var snapshot = DemoWorld.Load();

        // Assert
        Assert.Equal("fjord-1.0.0", snapshot.Version);
        Assert.NotEmpty(snapshot.ContentHash);

        var model = Assert.Single(snapshot.Models);
        Assert.Equal("FJORD", model.Code);
        Assert.Equal(3, model.Elements.Count);
        Assert.Contains(model.Elements, e => e.Code == "FJ2");
        Assert.Contains(model.Elements, e => e.Code == "FJ3");
        Assert.Contains(model.Elements, e => e.Code == "FJCH");

        Assert.Equal(2, snapshot.Markets.Count);
        Assert.Contains(snapshot.Markets, m => m.Code == "EUW");
        Assert.Contains(snapshot.Markets, m => m.Code == "EUN");
    }
}
