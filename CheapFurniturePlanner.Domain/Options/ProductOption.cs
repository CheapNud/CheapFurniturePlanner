using System.Text.Json.Serialization;

namespace CheapFurniturePlanner.Domain.Options;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ChoiceOption), "choice")]
[JsonDerivedType(typeof(FabricOption), "fabric")]
public abstract class ProductOption
{
    public int Id { get; set; }
    public required string OptionDefinitionCode { get; set; }
    public bool Required { get; set; } = true;
    public int DisplayIndex { get; set; }
    public List<VisibilityRule> VisibilityRules { get; set; } = [];
}
