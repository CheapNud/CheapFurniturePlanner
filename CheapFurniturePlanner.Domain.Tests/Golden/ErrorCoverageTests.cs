using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Golden;

// Provokes every PricingErrorKind at least once against the Fjord fixture (11 via the running
// engine; MissingBomSection has no engine code path today - see the dedicated fact below), plus
// the Task 9 review follow-up: a 2-element document where one element errors and one is valid
// still yields an errors-only result with a null Breakdown.
public class ErrorCoverageTests
{
    private static readonly Dictionary<string, string> BaselineChoices = new()
    {
        ["DEPTH"] = "STD",
        ["MECH"] = "NONE",
        ["STITCH"] = "PLAIN",
    };

    private static CatalogueSnapshot LoadMutatedSnapshot(Action<CatalogueSnapshot> mutate)
    {
        var snapshot = DemoWorld.Load();
        mutate(snapshot);
        return snapshot;
    }

    private static MarketParameters UnknownMarketParameters() =>
        new("US", 0m, 0m, [], new RoundingPolicy(2, 2, MidpointRounding.AwayFromZero, RoundStage.None));

    public static IEnumerable<object[]> Cases()
    {
        yield return
        [
            PricingErrorKind.IncompleteConfiguration,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = DemoWorld.Load();
                var selection = new ElementSelection("FJ2", 1, new Dictionary<string, string> { ["DEPTH"] = "STD", ["MECH"] = "NONE" }, "AQUA-BLUE");
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.UnknownModel,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = DemoWorld.Load();
                var request = new PricingRequest(snapshot, new ProductConfiguration("NOPE", []), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.UnknownElement,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = DemoWorld.Load();
                var selection = new ElementSelection("NOPE", 1, new Dictionary<string, string>(), null);
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.UnknownOptionSelection,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = DemoWorld.Load();
                var choices = new Dictionary<string, string>(BaselineChoices) { ["FOO"] = "BAR" };
                var selection = new ElementSelection("FJ2", 1, choices, "AQUA-BLUE");
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.SelectionViolatesVisibility,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = DemoWorld.Load();
                var choices = new Dictionary<string, string>(BaselineChoices) { ["HEAD"] = "HS1" };
                var selection = new ElementSelection("FJ2", 1, choices, "AQUA-BLUE");
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.UnknownFabricColor,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = DemoWorld.Load();
                var selection = new ElementSelection("FJ2", 1, new Dictionary<string, string>(BaselineChoices), "BOGUS-COLOR");
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.NoPriceGroupForMaterialKind,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = LoadMutatedSnapshot(s => s.PriceGroups = s.PriceGroups.Where(pg => pg.Code != "PGA").ToList());
                var selection = new ElementSelection("FJ2", 1, new Dictionary<string, string>(BaselineChoices), "AQUA-BLUE");
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.UnknownOperation,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = LoadMutatedSnapshot(s => s.Operations = s.Operations.Where(o => o.Code != "OP-CUT").ToList());
                var selection = new ElementSelection("FJ2", 1, new Dictionary<string, string>(BaselineChoices), "AQUA-BLUE");
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.UnknownMaterial,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = LoadMutatedSnapshot(s => s.Materials = s.Materials.Where(m => m.Code != "GLUE").ToList());
                var selection = new ElementSelection("FJ2", 1, new Dictionary<string, string>(BaselineChoices), "AQUA-BLUE");
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.UnknownFrameBody,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = LoadMutatedSnapshot(s => s.FrameBodies = s.FrameBodies.Where(f => f.Code != "FBX").ToList());
                var selection = new ElementSelection("FJ2", 1, new Dictionary<string, string>(BaselineChoices), "AQUA-BLUE");
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));
                return (snapshot, request);
            }),
        ];

        yield return
        [
            PricingErrorKind.UnknownMarket,
            (Func<(CatalogueSnapshot Snapshot, PricingRequest Request)>)(() =>
            {
                var snapshot = DemoWorld.Load();
                var selection = new ElementSelection("FJ2", 1, new Dictionary<string, string>(BaselineChoices), "AQUA-BLUE");
                var request = new PricingRequest(snapshot, new ProductConfiguration("FJORD", [selection]), new PricingContext(UnknownMarketParameters()));
                return (snapshot, request);
            }),
        ];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Calculate_InvalidRequest_ReturnsExpectedErrorKind(PricingErrorKind expectedKind, Func<(CatalogueSnapshot Snapshot, PricingRequest Request)> setup)
    {
        // Arrange
        var (_, request) = setup();

        // Act
        var result = PricingEngine.Calculate(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Breakdown);
        Assert.Contains(result.Errors, e => e.Kind == expectedKind);
    }

    // MissingBomSection is declared on PricingErrorKind but the current engine (ResolveStage /
    // CostStages / FinalizeStages) has no code path that raises it - it is reserved for a future
    // BOM-section-presence validation rule that doesn't exist yet. Documented in the Task 10
    // report as a gap rather than silently faked through a manufactured engine scenario.
    [Fact]
    public void PricingError_MissingBomSectionKind_IsConstructibleAndCarriesSubject()
    {
        // Arrange & Act
        var error = new PricingError(PricingErrorKind.MissingBomSection, "FJ2:Frame");

        // Assert
        Assert.Equal(PricingErrorKind.MissingBomSection, error.Kind);
        Assert.Equal("FJ2:Frame", error.Subject);
    }

    [Fact]
    public void Calculate_TwoElementDocumentOneErroringOneValid_ReturnsErrorsOnlyWithNullBreakdown()
    {
        // Arrange: FJ2 is a fully valid baseline selection; FJ3 is missing the required STITCH choice.
        var snapshot = DemoWorld.Load();
        var validSelection = new ElementSelection("FJ2", 1, new Dictionary<string, string>(BaselineChoices), "AQUA-BLUE");
        var invalidSelection = new ElementSelection("FJ3", 1, new Dictionary<string, string> { ["DEPTH"] = "STD", ["MECH"] = "NONE" }, "AQUA-BLUE");
        var configuration = new ProductConfiguration("FJORD", [validSelection, invalidSelection]);
        var request = new PricingRequest(snapshot, configuration, new PricingContext(snapshot.Markets.Single(m => m.Code == "EUW")));

        // Act
        var result = PricingEngine.Calculate(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Breakdown);
        Assert.Contains(result.Errors, e => e.Kind == PricingErrorKind.IncompleteConfiguration && e.Subject == "FJ3:STITCH");
    }
}
