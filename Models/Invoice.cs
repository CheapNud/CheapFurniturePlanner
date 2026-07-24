namespace CheapFurniturePlanner.Models;

// A real invoice - the entity the legacy system never had. Every line value is copied from the
// order's STORED prices and discounts at issue time (never re-derived from current masters), so
// invoice and order-entry math can never diverge. Issued invoices are immutable; corrections go
// through credit notes.
public class Invoice
{
    public int Id { get; set; }
    public required string InvoiceNumber { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime DueDate { get; set; }
    public decimal OrderDiscountPercent { get; set; }
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public required string CreatedByUserId { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
    public List<CreditNote> CreditNotes { get; set; } = [];
}
