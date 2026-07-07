using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Production;

// VariantEnumerator computes the full BOM-significant variant space of an element: every visibility-pruned
// choice combination crossed with every distinct material type, de-duplicated by the composed VariantCode.
public class VariantEnumeratorTests
{
    private static Element FjchElement(CatalogueSnapshot snapshot) =>
        snapshot.Models.Single(m => m.Code == "FJORD").Elements.Single(e => e.Code == "FJCH");

    [Fact]
    public void Enumerate_EveryDescriptor_HasSelfConsistentVariantCode()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var element = FjchElement(snapshot);

        // Act
        var descriptors = VariantEnumerator.Enumerate(element, snapshot);

        // Assert
        Assert.NotEmpty(descriptors);
        foreach (var descriptor in descriptors)
        {
            var choiceSelections = descriptor.BomSignificantSelections
                .Where(kv => kv.Key != VariantCode.MaterialDefCode)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            var selection = new ElementSelection(element.Code, 1, choiceSelections, null);
            var recomposed = VariantCode.From(element, selection, descriptor.MaterialTypeCode);
            Assert.Equal(recomposed, descriptor.VariantCode);
        }
    }

    [Fact]
    public void Enumerate_HeadrestChoice_OnlyAppearsUnderMechRecTrigger()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var element = FjchElement(snapshot);

        // Act
        var descriptors = VariantEnumerator.Enumerate(element, snapshot);

        // Assert
        Assert.All(descriptors, d =>
        {
            if (d.BomSignificantSelections.ContainsKey("HEAD"))
            {
                Assert.Equal("REC", d.BomSignificantSelections["MECH"]);
            }
        });
        Assert.Contains(descriptors, d => d.BomSignificantSelections.ContainsKey("HEAD"));
    }

    [Fact]
    public void Enumerate_MaterialTypesMultiplyTheSet_ForTheSameChoiceCombo()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var element = FjchElement(snapshot);

        // Act
        var descriptors = VariantEnumerator.Enumerate(element, snapshot);

        // Assert
        var sameCombo = descriptors
            .Where(d => d.BomSignificantSelections["DEPTH"] == "STD" && d.BomSignificantSelections["MECH"] == "NONE")
            .ToList();
        var materialTypes = sameCombo.Select(d => d.MaterialTypeCode).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.Equal(["Fabric", "LEATHER-THICK", "Leather"], materialTypes);
        Assert.Equal(materialTypes.Count, sameCombo.Select(d => d.VariantCode).Distinct().Count());
    }

    [Fact]
    public void Enumerate_NoVisibleFabricOption_YieldsNullMaterialForEveryDescriptor()
    {
        // Arrange: a minimal synthetic element with a single BOM-significant choice and no fabric option at all.
        var element = new Element
        {
            Code = "NOFAB",
            Name = "No Fabric Element",
            Options =
            [
                new ChoiceOption
                {
                    OptionDefinitionCode = "FINISH",
                    DisplayIndex = 0,
                    AffectsBom = true,
                    Values =
                    [
                        new ProductOptionValue { OptionChoiceCode = "MATTE", DisplayIndex = 0, IsDefault = true },
                        new ProductOptionValue { OptionChoiceCode = "GLOSS", DisplayIndex = 1, IsDefault = false },
                    ],
                },
            ],
        };
        var snapshot = new CatalogueSnapshot { Version = "test" };

        // Act
        var descriptors = VariantEnumerator.Enumerate(element, snapshot);

        // Assert
        Assert.Equal(2, descriptors.Count);
        Assert.All(descriptors, d => Assert.Null(d.MaterialTypeCode));
        Assert.All(descriptors, d => Assert.False(d.BomSignificantSelections.ContainsKey(VariantCode.MaterialDefCode)));
    }

    [Fact]
    public void Enumerate_ProducesNoDuplicateVariantCodes_OrderedOrdinally()
    {
        // Arrange
        var snapshot = DemoWorld.Load();
        var element = FjchElement(snapshot);

        // Act
        var descriptors = VariantEnumerator.Enumerate(element, snapshot);

        // Assert
        var codes = descriptors.Select(d => d.VariantCode).ToList();
        Assert.Equal(codes.Distinct().Count(), codes.Count);
        Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal).ToList(), codes);
    }
}
