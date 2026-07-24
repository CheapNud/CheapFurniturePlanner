namespace CheapFurniturePlanner.Models;

public class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int OrderLineId { get; set; }
    public required string Description { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal LineTotal { get; set; }
    public decimal VatRatePercent { get; set; }
    public decimal VatAmount { get; set; }
}
