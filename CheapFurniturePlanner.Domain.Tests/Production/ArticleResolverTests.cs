using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Production;

// OE-1 Task 1: the forward half of the article bridge — a configuration's composed variant code
// resolves to its catalogue-backed Article by string equality (the legacy value-set subset match
// collapsed into one key; explicit Selections are stored for the reverse direction and a future
// value-set migration).
public class ArticleResolverTests
{
    private static CatalogueSnapshot SnapshotWith(params Article[] articles) => new()
    {
        Version = "",
        Articles = [.. articles],
    };

    private static Article Backed(string assigned, string modelCode, string elementCode, string variantCode) => new()
    {
        Id = 1,
        AssignedCode = assigned,
        ModelCode = modelCode,
        ElementCode = elementCode,
        VariantCode = variantCode,
        Selections = new Dictionary<string, string> { ["FEET"] = "ELEC" },
    };

    [Fact]
    public void ResolveByConfiguration_Hit_ReturnsArticle()
    {
        var snapshot = SnapshotWith(Backed("K7E", "M-A", "E-A", "E-A-FEET:ELEC"));

        var article = ArticleResolver.ResolveByConfiguration(snapshot, "E-A", "E-A-FEET:ELEC");

        Assert.NotNull(article);
        Assert.Equal("K7E", article!.AssignedCode);
    }

    [Fact]
    public void ResolveByConfiguration_Miss_ReturnsNull()
    {
        var snapshot = SnapshotWith(Backed("K7E", "M-A", "E-A", "E-A-FEET:ELEC"));

        Assert.Null(ArticleResolver.ResolveByConfiguration(snapshot, "E-A", "E-A-FEET:MAN"));
        Assert.Null(ArticleResolver.ResolveByConfiguration(snapshot, "E-B", "E-A-FEET:ELEC"));
    }

    [Fact]
    public void ResolveByConfiguration_IgnoresStandaloneArticles()
    {
        var standalone = new Article { Id = 2, AssignedCode = "ART-LEGACY", Name = "Old sofa", ManualPrice = 100m };
        var snapshot = SnapshotWith(standalone);

        Assert.Null(ArticleResolver.ResolveByConfiguration(snapshot, "ART-LEGACY", "ART-LEGACY"));
        Assert.False(standalone.IsCatalogueBacked());
    }
}
