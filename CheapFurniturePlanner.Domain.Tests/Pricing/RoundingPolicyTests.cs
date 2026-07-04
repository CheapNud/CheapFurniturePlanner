using CheapFurniturePlanner.Domain.Pricing;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Pricing;

public class RoundingPolicyTests
{
    [Fact]
    public void FinalOnlyPolicy_LeavesLinesUnrounded_RoundsFinalToZeroAwayFromZero()
    {
        // Arrange
        var policy = new RoundingPolicy(
            LineDecimals: 2,
            FinalDecimals: 0,
            Midpoint: System.MidpointRounding.AwayFromZero,
            Stages: RoundStage.Final
        );

        decimal lineValue = 10.126m;
        decimal finalValue = 10.156m;

        // Act
        var roundedLine = policy.RoundLine(lineValue);
        var roundedFinal = policy.RoundFinal(finalValue);

        // Assert
        Assert.Equal(10.126m, roundedLine); // Unrounded
        Assert.Equal(10m, roundedFinal); // Rounded to 0 decimals AwayFromZero
    }

    [Fact]
    public void LineStagePolicy_RoundsLinesToTwoDecimals()
    {
        // Arrange
        var policy = new RoundingPolicy(
            LineDecimals: 2,
            FinalDecimals: 0,
            Midpoint: System.MidpointRounding.AwayFromZero,
            Stages: RoundStage.Line
        );

        decimal lineValue = 10.126m;

        // Act
        var roundedLine = policy.RoundLine(lineValue);

        // Assert
        Assert.Equal(10.13m, roundedLine); // Rounded to 2 decimals
    }

    [Fact]
    public void NoStages_RoundsNothing()
    {
        // Arrange
        var policy = new RoundingPolicy(
            LineDecimals: 2,
            FinalDecimals: 0,
            Midpoint: System.MidpointRounding.AwayFromZero,
            Stages: RoundStage.None
        );

        decimal lineValue = 10.126m;
        decimal finalValue = 10.156m;

        // Act
        var roundedLine = policy.RoundLine(lineValue);
        var roundedFinal = policy.RoundFinal(finalValue);
        var roundedSubtotal = policy.RoundSubtotal(finalValue);

        // Assert
        Assert.Equal(10.126m, roundedLine); // Unrounded
        Assert.Equal(10.156m, roundedFinal); // Unrounded
        Assert.Equal(10.156m, roundedSubtotal); // Unrounded
    }
}
