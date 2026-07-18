using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Pure resolver tests, no DB - build rule lists inline.
public class DiscountResolverTests
{
    private static DiscountRule Rule(DiscountScope scope, string? collectionCode = null,
        string? elementCode = null, string? priceGroupCode = null, string? modelCode = null,
        string? modelTypeCode = null, string? materialTypeCode = null,
        decimal? ratePercent = null, decimal? fixedPrice = null) => new()
    {
        SellerId = 1,
        Scope = scope,
        CollectionCode = collectionCode,
        ElementCode = elementCode,
        PriceGroupCode = priceGroupCode,
        ModelCode = modelCode,
        ModelTypeCode = modelTypeCode,
        MaterialTypeCode = materialTypeCode,
        RatePercent = ratePercent,
        FixedPrice = fixedPrice,
    };

    [Fact]
    public void ElementPriceGroup_BeatsModel_WhenBothMatch()
    {
        var rules = new List<DiscountRule>
        {
            Rule(DiscountScope.ElementPriceGroup, elementCode: "EA", priceGroupCode: "PGA", ratePercent: 10m),
            Rule(DiscountScope.Model, modelCode: "M1", ratePercent: 20m),
        };

        var result = DiscountResolver.Suggest(rules, null, "M1", null, "EA", "PGA", null);

        Assert.NotNull(result);
        Assert.Equal(DiscountScope.ElementPriceGroup, result!.Scope);
        Assert.Equal(10m, result.RatePercent);
    }

    [Fact]
    public void Model_BeatsModelType_WhenBothMatch()
    {
        var rules = new List<DiscountRule>
        {
            Rule(DiscountScope.Model, modelCode: "M1", ratePercent: 10m),
            Rule(DiscountScope.ModelType, modelTypeCode: "MT-RELAX", ratePercent: 20m),
        };

        var result = DiscountResolver.Suggest(rules, null, "M1", "MT-RELAX", "EA", "PGA", null);

        Assert.NotNull(result);
        Assert.Equal(DiscountScope.Model, result!.Scope);
    }

    [Fact]
    public void ModelType_BeatsMaterialType_WhenBothMatch()
    {
        var rules = new List<DiscountRule>
        {
            Rule(DiscountScope.ModelType, modelTypeCode: "MT-RELAX", ratePercent: 10m),
            Rule(DiscountScope.MaterialType, materialTypeCode: "LEATHER-THICK", ratePercent: 20m),
        };

        var result = DiscountResolver.Suggest(rules, null, "M1", "MT-RELAX", "EA", "PGA", "LEATHER-THICK");

        Assert.NotNull(result);
        Assert.Equal(DiscountScope.ModelType, result!.Scope);
    }

    [Fact]
    public void MaterialType_BeatsEverything_WhenBothMatch()
    {
        var rules = new List<DiscountRule>
        {
            Rule(DiscountScope.MaterialType, materialTypeCode: "LEATHER-THICK", ratePercent: 10m),
            Rule(DiscountScope.Everything, ratePercent: 20m),
        };

        var result = DiscountResolver.Suggest(rules, null, "M1", "MT-RELAX", "EA", "PGA", "LEATHER-THICK");

        Assert.NotNull(result);
        Assert.Equal(DiscountScope.MaterialType, result!.Scope);
    }

    [Fact]
    public void CollectionSpecific_BeatsWildcard_WithinSameTier()
    {
        var rules = new List<DiscountRule>
        {
            Rule(DiscountScope.Model, modelCode: "M1", ratePercent: 5m),
            Rule(DiscountScope.Model, collectionCode: "COLA", modelCode: "M1", ratePercent: 15m),
        };

        var result = DiscountResolver.Suggest(rules, "COLA", "M1", null, "EA", "PGA", null);

        Assert.NotNull(result);
        Assert.Equal(15m, result!.RatePercent);
    }

    [Fact]
    public void ModelTypeTier_Skipped_WhenModelTypeCodeNull_EvenIfRuleExists()
    {
        var rules = new List<DiscountRule>
        {
            Rule(DiscountScope.ModelType, modelTypeCode: "MT-RELAX", ratePercent: 10m),
            Rule(DiscountScope.MaterialType, materialTypeCode: "LEATHER-THICK", ratePercent: 20m),
        };

        var result = DiscountResolver.Suggest(rules, null, "M1", null, "EA", "PGA", "LEATHER-THICK");

        Assert.NotNull(result);
        Assert.Equal(DiscountScope.MaterialType, result!.Scope);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        var rules = new List<DiscountRule>
        {
            Rule(DiscountScope.Model, modelCode: "M2", ratePercent: 10m),
        };

        var result = DiscountResolver.Suggest(rules, null, "M1", null, "EA", "PGA", null);

        Assert.Null(result);
    }

    [Fact]
    public void WildcardWins_WhenSpecificRuleIsForDifferentCollection()
    {
        var rules = new List<DiscountRule>
        {
            Rule(DiscountScope.Everything, collectionCode: "COL-OTHER", ratePercent: 20m),
            Rule(DiscountScope.Everything, ratePercent: 5m),
        };

        var result = DiscountResolver.Suggest(rules, "COL-MINE", "M1", null, "EA", null, null);

        Assert.NotNull(result);
        Assert.Equal(5m, result!.RatePercent);
    }

    [Fact]
    public void FixedPriceRule_Surfaces_WithElementPriceGroupScope()
    {
        var rules = new List<DiscountRule>
        {
            Rule(DiscountScope.ElementPriceGroup, elementCode: "EA", priceGroupCode: "PGA", fixedPrice: 42.5m),
        };

        var result = DiscountResolver.Suggest(rules, null, "M1", null, "EA", "PGA", null);

        Assert.NotNull(result);
        Assert.Equal(DiscountScope.ElementPriceGroup, result!.Scope);
        Assert.Equal(42.5m, result.FixedPrice);
        Assert.Null(result.RatePercent);
    }
}
