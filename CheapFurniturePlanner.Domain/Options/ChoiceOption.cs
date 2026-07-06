namespace CheapFurniturePlanner.Domain.Options;

public class ChoiceOption : ProductOption
{
    public bool AffectsBom { get; set; }
    public List<ProductOptionValue> Values { get; set; } = [];
}
