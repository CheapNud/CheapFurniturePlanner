namespace CheapFurniturePlanner.Models;

public enum CreditReason { Return, Goodwill, PriceCorrection }

// A monetary credit against an issued invoice (the legacy version was an empty shell holding an
// uploaded PDF). Amounts are split from the gross at the invoice's blended VAT rate; the sum of
// credits can never exceed the invoice's gross total.
public class CreditNote
{
    public int Id { get; set; }
    public required string CreditNoteNumber { get; set; }
    public int InvoiceId { get; set; }
    public CreditReason Reason { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public string? Note { get; set; }
    public DateTime IssuedAt { get; set; }
    public bool IsSettled { get; set; }
    public DateTime? SettledAt { get; set; }
    public required string CreatedByUserId { get; set; }
}
