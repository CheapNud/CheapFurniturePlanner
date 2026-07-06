using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Pricing;

public class VariantCodeTests
{
    [Fact]
    public void From_IdenticalChoiceSelectionsWithDifferentInsertionOrder_GeneratesSameVariantString()
    {
        // Arrange
        var element = new Element
        {
            Code = "SOFA-001",
            Name = "Sofa",
            Options =
            [
                new ChoiceOption
                {
                    OptionDefinitionCode = "COLOR",
                    AffectsBom = true,
                    Values = []
                },
                new ChoiceOption
                {
                    OptionDefinitionCode = "SIZE",
                    AffectsBom = true,
                    Values = []
                }
            ]
        };

        var selectionOrder1 = new ElementSelection(
            ElementCode: "SOFA-001",
            Quantity: 1,
            ChoiceSelections: new Dictionary<string, string>
            {
                { "COLOR", "RED" },
                { "SIZE", "LARGE" }
            },
            FabricColorCode: null
        );

        var selectionOrder2 = new ElementSelection(
            ElementCode: "SOFA-001",
            Quantity: 1,
            ChoiceSelections: new Dictionary<string, string>
            {
                { "SIZE", "LARGE" },
                { "COLOR", "RED" }
            },
            FabricColorCode: null
        );

        // Act
        var variant1 = VariantCode.From(element, selectionOrder1);
        var variant2 = VariantCode.From(element, selectionOrder2);

        // Assert
        Assert.Equal(variant1, variant2);
    }

    [Fact]
    public void From_NonBomAffectingOptionsAndFabricColor_DoesNotAppearInVariantString()
    {
        // Arrange
        var element = new Element
        {
            Code = "CHAIR-001",
            Name = "Chair",
            Options =
            [
                new ChoiceOption
                {
                    OptionDefinitionCode = "FRAME",
                    AffectsBom = true,
                    Values = []
                },
                new ChoiceOption
                {
                    OptionDefinitionCode = "FABRIC_PATTERN",
                    AffectsBom = false,
                    Values = []
                }
            ]
        };

        var selection = new ElementSelection(
            ElementCode: "CHAIR-001",
            Quantity: 1,
            ChoiceSelections: new Dictionary<string, string>
            {
                { "FRAME", "WOOD" },
                { "FABRIC_PATTERN", "STRIPE" }
            },
            FabricColorCode: "BLUE"
        );

        // Act
        var variant = VariantCode.From(element, selection);

        // Assert
        Assert.Equal("CHAIR-001-FRAME:WOOD", variant);
        Assert.DoesNotContain("FABRIC_PATTERN", variant);
        Assert.DoesNotContain("BLUE", variant);
    }

    [Fact]
    public void From_DifferentBomSignificantChoice_GeneratesDifferentVariantString()
    {
        // Arrange
        var element = new Element
        {
            Code = "TABLE-001",
            Name = "Table",
            Options =
            [
                new ChoiceOption
                {
                    OptionDefinitionCode = "MATERIAL",
                    AffectsBom = true,
                    Values = []
                }
            ]
        };

        var selection1 = new ElementSelection(
            ElementCode: "TABLE-001",
            Quantity: 1,
            ChoiceSelections: new Dictionary<string, string>
            {
                { "MATERIAL", "WOOD" }
            },
            FabricColorCode: null
        );

        var selection2 = new ElementSelection(
            ElementCode: "TABLE-001",
            Quantity: 1,
            ChoiceSelections: new Dictionary<string, string>
            {
                { "MATERIAL", "METAL" }
            },
            FabricColorCode: null
        );

        // Act
        var variant1 = VariantCode.From(element, selection1);
        var variant2 = VariantCode.From(element, selection2);

        // Assert
        Assert.NotEqual(variant1, variant2);
        Assert.Equal("TABLE-001-MATERIAL:WOOD", variant1);
        Assert.Equal("TABLE-001-MATERIAL:METAL", variant2);
    }

    [Fact]
    public void From_ElementWithZeroBomAffectingOptions_ReturnsElementCodeOnly()
    {
        // Arrange
        var element = new Element
        {
            Code = "SHELF-001",
            Name = "Shelf",
            Options =
            [
                new ChoiceOption
                {
                    OptionDefinitionCode = "COLOR",
                    AffectsBom = false,
                    Values = []
                }
            ]
        };

        var selection = new ElementSelection(
            ElementCode: "SHELF-001",
            Quantity: 1,
            ChoiceSelections: new Dictionary<string, string>
            {
                { "COLOR", "WHITE" }
            },
            FabricColorCode: null
        );

        // Act
        var variant = VariantCode.From(element, selection);

        // Assert
        Assert.Equal("SHELF-001", variant);
    }
}
