using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Options;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Catalog;

public class EntityConstructionTests
{
    [Fact]
    public void FurnitureModel_WithElementAndOptions_ConstructsSuccessfully()
    {
        // Arrange & Act
        var element = new Element
        {
            Code = "EL-001",
            Name = "Base Element",
            Width = 100.0,
            Depth = 50.0,
            Height = 75.0,
            TransportUnits = 2
        };

        var choice1 = new ProductOptionValue
        {
            OptionChoiceCode = "CHOICE-1",
            DisplayIndex = 0,
            IsDefault = true
        };

        var choice2 = new ProductOptionValue
        {
            OptionChoiceCode = "CHOICE-2",
            DisplayIndex = 1,
            IsDefault = false
        };

        var visibilityRule = new VisibilityRule(
            TriggerOptionDefinitionCode: "OPT-DEF-1",
            TriggerChoiceCode: "CHOICE-1",
            RevealedOptionDefinitionCode: "OPT-DEF-2"
        );

        var choiceOption = new ChoiceOption
        {
            OptionDefinitionCode = "OPT-DEF-1",
            AffectsBom = true,
            Values = [choice1, choice2],
            VisibilityRules = [visibilityRule]
        };

        var fabricOption = new FabricOption
        {
            OptionDefinitionCode = "FABRIC-OPT-1",
            IsPriceDetermining = true,
            FabricGroupCodes = ["FG-1", "FG-2"]
        };

        element.Options = [choiceOption, fabricOption];

        var furnitureModel = new FurnitureModel
        {
            Code = "FM-001",
            Name = "Test Furniture Model",
            CollectionCode = "COLL-001",
            Elements = [element]
        };

        // Assert
        Assert.Equal("FM-001", furnitureModel.Code);
        Assert.Equal("Test Furniture Model", furnitureModel.Name);
        Assert.Equal("COLL-001", furnitureModel.CollectionCode);
        Assert.Equal(TradeItemState.Active, furnitureModel.State);
        Assert.NotNull(furnitureModel.Elements);
        Assert.Single(furnitureModel.Elements);

        Assert.Equal("EL-001", element.Code);
        Assert.Equal("Base Element", element.Name);
        Assert.Equal(100.0, element.Width);
        Assert.Equal(50.0, element.Depth);
        Assert.Equal(75.0, element.Height);
        Assert.Equal(2, element.TransportUnits);
        Assert.Equal(TradeItemState.Active, element.State);
        Assert.NotNull(element.Options);
        Assert.Equal(2, element.Options.Count);

        Assert.Equal("OPT-DEF-1", choiceOption.OptionDefinitionCode);
        Assert.True(choiceOption.Required);
        Assert.True(choiceOption.AffectsBom);
        Assert.NotNull(choiceOption.Values);
        Assert.Equal(2, choiceOption.Values.Count);
        Assert.Single(choiceOption.VisibilityRules);

        Assert.Equal("FABRIC-OPT-1", fabricOption.OptionDefinitionCode);
        Assert.True(fabricOption.Required);
        Assert.True(fabricOption.IsPriceDetermining);
        Assert.NotNull(fabricOption.FabricGroupCodes);
        Assert.Equal(2, fabricOption.FabricGroupCodes.Count);

        var rule = visibilityRule;
        Assert.Equal("OPT-DEF-1", rule.TriggerOptionDefinitionCode);
        Assert.Equal("CHOICE-1", rule.TriggerChoiceCode);
        Assert.Equal("OPT-DEF-2", rule.RevealedOptionDefinitionCode);
    }
}
