namespace CheapFurniturePlanner.Domain.Options;

public class ProductOptionValue
{
    public int Id { get; set; }
    public required string OptionChoiceCode { get; set; }
    public int DisplayIndex { get; set; }
    public bool IsDefault { get; set; }
}
