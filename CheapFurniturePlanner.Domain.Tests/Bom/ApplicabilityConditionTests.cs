using CheapFurniturePlanner.Domain.Bom;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Bom;

public class ApplicabilityConditionTests
{
    [Fact]
    public void EmptyCondition_IsSatisfiedByAnySelections()
    {
        // Arrange
        var condition = new ApplicabilityCondition(new List<SelectionKey>());
        var selections = new Dictionary<string, string>
        {
            { "Color", "Red" },
            { "Size", "Large" }
        };

        // Act
        var result = condition.IsSatisfiedBy(selections);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void SingleSelectionMatch_IsSatisfied()
    {
        // Arrange
        var condition = new ApplicabilityCondition(
            new List<SelectionKey> { new("Color", "Red") }
        );
        var selections = new Dictionary<string, string> { { "Color", "Red" } };

        // Act
        var result = condition.IsSatisfiedBy(selections);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void SingleSelectionMismatch_IsNotSatisfied()
    {
        // Arrange
        var condition = new ApplicabilityCondition(
            new List<SelectionKey> { new("Color", "Red") }
        );
        var selections = new Dictionary<string, string> { { "Color", "Blue" } };

        // Act
        var result = condition.IsSatisfiedBy(selections);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MissingSelectionKey_IsNotSatisfied()
    {
        // Arrange
        var condition = new ApplicabilityCondition(
            new List<SelectionKey> { new("Color", "Red") }
        );
        var selections = new Dictionary<string, string> { { "Size", "Large" } };

        // Act
        var result = condition.IsSatisfiedBy(selections);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MultipleSelectionsAllMatch_IsSatisfied()
    {
        // Arrange
        var condition = new ApplicabilityCondition(
            new List<SelectionKey>
            {
                new("Color", "Red"),
                new("Size", "Large"),
                new("Material", "Leather")
            }
        );
        var selections = new Dictionary<string, string>
        {
            { "Color", "Red" },
            { "Size", "Large" },
            { "Material", "Leather" }
        };

        // Act
        var result = condition.IsSatisfiedBy(selections);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MultipleSelectionsPartialMatch_IsNotSatisfied()
    {
        // Arrange
        var condition = new ApplicabilityCondition(
            new List<SelectionKey>
            {
                new("Color", "Red"),
                new("Size", "Large"),
                new("Material", "Leather")
            }
        );
        var selections = new Dictionary<string, string>
        {
            { "Color", "Red" },
            { "Size", "Large" },
            { "Material", "Fabric" }
        };

        // Act
        var result = condition.IsSatisfiedBy(selections);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MultipleSelectionsOneKeyMissing_IsNotSatisfied()
    {
        // Arrange
        var condition = new ApplicabilityCondition(
            new List<SelectionKey>
            {
                new("Color", "Red"),
                new("Size", "Large"),
                new("Material", "Leather")
            }
        );
        var selections = new Dictionary<string, string>
        {
            { "Color", "Red" },
            { "Size", "Large" }
        };

        // Act
        var result = condition.IsSatisfiedBy(selections);

        // Assert
        Assert.False(result);
    }
}
