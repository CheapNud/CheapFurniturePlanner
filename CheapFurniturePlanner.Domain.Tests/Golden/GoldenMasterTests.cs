using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Golden;

// Byte-exact comparisons of PricingEngine output against hand-verified expected JSON for every
// named scenario in golden-cases.json. See .superpowers/sdd/task-10-report.md for the hand
// computations that back these fixtures.
public class GoldenMasterTests
{
    public static IEnumerable<object[]> Cases() => GoldenCaseLoader.LoadCases().Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Calculate_GoldenCase_MatchesHandVerifiedExpectedJson(string caseName)
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var goldenCase = GoldenCaseLoader.LoadCases().Single(c => c.Name == caseName);
        var request = GoldenCaseLoader.BuildRequest(goldenCase, snapshot);
        var expectedJson = GoldenCaseLoader.LoadExpectedJson(caseName);

        // Act
        var result = PricingEngine.Calculate(request);

        // Assert
        Assert.True(result.IsSuccess, $"Expected success but got errors: {string.Join(", ", result.Errors.Select(e => $"{e.Kind}:{e.Subject}"))}");
        var actualJson = CanonicalJson.Serialize(result.Breakdown);
        Assert.Equal(expectedJson, actualJson);
    }
}
