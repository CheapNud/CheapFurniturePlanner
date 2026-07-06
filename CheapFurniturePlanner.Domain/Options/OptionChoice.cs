using CheapFurniturePlanner.Domain.Catalog;

namespace CheapFurniturePlanner.Domain.Options;

public class OptionChoice : TradeItem
{
    public required string OptionDefinitionCode { get; set; }
    public required string Name { get; set; }
    public string? SupplierNumber { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }
}
