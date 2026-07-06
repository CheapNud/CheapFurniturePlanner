using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Pricing;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Pricing;

// MaterialTypeCode is the BOM-significant material axis derivation (fabric vs leather vs thick-leather);
// this exercises the standalone extraction independent of the wider ResolveStage/PricingEngine flow.
public class MaterialResolutionTests
{
    [Fact]
    public void MaterialTypeCode_SentinelPriceGroup_ReturnsNull()
    {
        var priceGroup = new PriceGroup { Code = "", Kind = MaterialKind.Fabric };

        var materialTypeCode = MaterialResolution.MaterialTypeCode(priceGroup);

        Assert.Null(materialTypeCode);
    }

    [Fact]
    public void MaterialTypeCode_LeatherWithoutExplicitMaterialTypeCode_FallsBackToKind()
    {
        var priceGroup = new PriceGroup { Code = "PGL", Kind = MaterialKind.Leather };

        var materialTypeCode = MaterialResolution.MaterialTypeCode(priceGroup);

        Assert.Equal("Leather", materialTypeCode);
    }

    [Fact]
    public void MaterialTypeCode_ThickLeatherWithExplicitMaterialTypeCode_ReturnsExplicitCode()
    {
        var priceGroup = new PriceGroup { Code = "PGLT", Kind = MaterialKind.Leather, MaterialTypeCode = "LEATHER-THICK" };

        var materialTypeCode = MaterialResolution.MaterialTypeCode(priceGroup);

        Assert.Equal("LEATHER-THICK", materialTypeCode);
    }
}
