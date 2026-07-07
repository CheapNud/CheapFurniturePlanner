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

        Assert.Equal(2, snapshot.Models.Count);
        var model = Assert.Single(snapshot.Models, m => m.Code == "FJORD");
        Assert.Equal(3, model.Elements.Count);
        Assert.Contains(model.Elements, e => e.Code == "FJ2");
        Assert.Contains(model.Elements, e => e.Code == "FJ3");
        Assert.Contains(model.Elements, e => e.Code == "FJCH");

        var studio = Assert.Single(snapshot.Models, m => m.Code == "FJORD-STUDIO");
        Assert.Equal(3, studio.Elements.Count);
        Assert.Contains(studio.Elements, e => e.Code == "FS2");
        Assert.Contains(studio.Elements, e => e.Code == "FS3");
        Assert.Contains(studio.Elements, e => e.Code == "FSCH");

        Assert.Equal(2, snapshot.Markets.Count);
        Assert.Contains(snapshot.Markets, m => m.Code == "EUW");
        Assert.Contains(snapshot.Markets, m => m.Code == "EUN");
    }
}
