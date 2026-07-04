namespace CheapFurniturePlanner.Domain.Options;

public class FabricOption : ProductOption
{
    public bool IsPriceDetermining { get; set; } = true;
    public List<string> FabricGroupCodes { get; set; } = [];
}
