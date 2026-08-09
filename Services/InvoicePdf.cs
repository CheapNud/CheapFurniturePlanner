using System.Globalization;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapHelpers.Services.DataExchange.Pdf;
using CheapHelpers.Services.DataExchange.Pdf.Configuration;
using CheapHelpers.Services.DataExchange.Pdf.Templates;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Invoice/credit-note rendering through the CheapHelpers PDF stack. Rendering only - documents
// are already immutable rows; nothing here mutates state. All money is formatted with the
// invariant culture (the legacy exporter emitted locale commas that broke the downstream parse).
public sealed class InvoicePdf(IDbContextFactory<FurniturePlannerContext> factory, IPdfExportService exporter, string outputRoot)
{
    private sealed record DocumentRow(string Description, string Quantity, string UnitPrice, string DiscountPercent, string Amount);

    public async Task<string> GenerateInvoiceAsync(int invoiceId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Order)!.ThenInclude(o => o!.Consumer)!.ThenInclude(c => c!.PrimaryAddress)!.ThenInclude(a => a!.Region)
            .Include(i => i.Order)!.ThenInclude(o => o!.Seller)!.ThenInclude(s => s!.Address)!.ThenInclude(a => a!.Region)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");

        var firm = await IssuingFirmAsync(db, invoice.Order?.FirmId, ct);
        List<DocumentRow> issuerRows = [];
        if (firm is not null)
        {
            issuerRows.Add(new("Invoiced by", firm.Name, "", "", ""));
            if (!string.IsNullOrWhiteSpace(firm.VatNumber)) { issuerRows.Add(new("VAT", firm.VatNumber, "", "", "")); }
            if (firm.Address is not null) { issuerRows.Add(new("Firm address", firm.Address.ToOneLine(), "", "", "")); }
            if (!string.IsNullOrWhiteSpace(firm.Iban))
            {
                issuerRows.Add(new("IBAN", string.IsNullOrWhiteSpace(firm.Bic) ? firm.Iban : $"{firm.Iban} ({firm.Bic})", "", "", ""));
            }
        }

        List<DocumentRow> rows =
        [
            .. issuerRows,
            new("Order", invoice.Order!.OrderNumber, "", "", ""),
            new("Seller", invoice.Order.Seller?.Name ?? "", "", "", ""),
            new("Consumer", invoice.Order.Consumer?.Name ?? "", "", "", ""),
        ];
        if (!string.IsNullOrWhiteSpace(invoice.Order.Consumer?.VatNumber))
        {
            rows.Add(new("Consumer VAT", invoice.Order.Consumer.VatNumber, "", "", ""));
        }
        var sellerAddress = invoice.Order.Seller?.Address;
        if (sellerAddress is not null) { rows.Add(new("Seller address", sellerAddress.ToOneLine(), "", "", "")); }
        var buyerAddress = await BuyerAddressAsync(db, invoice.Order.Consumer, ct);
        if (buyerAddress is not null) { rows.Add(new("Consumer address", buyerAddress.ToOneLine(), "", "", "")); }
        rows.Add(new("Issued", invoice.IssuedAt.ToString("yyyy-MM-dd"), "", "", ""));
        rows.Add(new("Due", invoice.DueDate.ToString("yyyy-MM-dd"), "", "", ""));
        rows.Add(new("", "", "", "", ""));
        rows.AddRange(invoice.Lines.Select(line => new DocumentRow(
            line.Description,
            line.Quantity.ToString(CultureInfo.InvariantCulture),
            Money(line.UnitPrice),
            line.DiscountPercent == 0 ? "" : Money(line.DiscountPercent),
            Money(line.LineTotal))));
        rows.Add(new("", "", "", "", ""));
        if (invoice.OrderDiscountPercent != 0)
        {
            rows.Add(new("Order discount %", "", "", "", Money(invoice.OrderDiscountPercent)));
        }
        rows.Add(new("Net total", "", "", "", Money(invoice.NetTotal)));
        rows.Add(new($"VAT ({Money(invoice.Lines.FirstOrDefault()?.VatRatePercent ?? 0)}%)", "", "", "", Money(invoice.VatTotal)));
        rows.Add(new("Total due", "", "", "", Money(invoice.GrossTotal)));

        return await WriteAsync($"Invoice {invoice.InvoiceNumber}", invoice.InvoiceNumber, rows);
    }

    public async Task<string> GenerateCreditNoteAsync(int creditNoteId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var creditNote = await db.CreditNotes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == creditNoteId, ct)
            ?? throw new InvalidOperationException($"Credit note {creditNoteId} not found.");
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Order)!.ThenInclude(o => o!.Consumer)
            .FirstAsync(i => i.Id == creditNote.InvoiceId, ct);

        List<DocumentRow> rows =
        [
            new("Invoice", invoice.InvoiceNumber, "", "", ""),
            new("Consumer", invoice.Order!.Consumer?.Name ?? "", "", "", ""),
            new("Reason", creditNote.Reason.ToString(), "", "", ""),
            new("Issued", creditNote.IssuedAt.ToString("yyyy-MM-dd"), "", "", ""),
        ];
        if (creditNote.Note is not null) { rows.Add(new("Note", creditNote.Note, "", "", "")); }
        rows.Add(new("", "", "", "", ""));
        rows.Add(new("Net", "", "", "", Money(creditNote.NetAmount)));
        rows.Add(new("VAT", "", "", "", Money(creditNote.VatAmount)));
        rows.Add(new("Total credited", "", "", "", Money(creditNote.GrossAmount)));

        return await WriteAsync($"Credit note {creditNote.CreditNoteNumber}", creditNote.CreditNoteNumber, rows);
    }

    private async Task<string> WriteAsync(string title, string documentNumber, List<DocumentRow> rows)
    {
        // Header/footer off: the library header prints literal company placeholders. IsBold off
        // everywhere: the packaged renderer duplicates bold cell text.
        var template = new PdfDocumentTemplate
        {
            Title = title,
            UseHeader = false,
            UseFooter = false,
            Columns =
            [
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Description), DisplayName = "Description", Width = 3f, FontSize = 9 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Quantity), DisplayName = "Qty", Width = 1f, FontSize = 9 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.UnitPrice), DisplayName = "Unit", Width = 1f, FontSize = 9 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.DiscountPercent), DisplayName = "Disc %", Width = 1f, FontSize = 9 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Amount), DisplayName = "Amount", Width = 1f, FontSize = 9 },
            ],
        };
        Directory.CreateDirectory(outputRoot);
        var filePath = Path.Combine(outputRoot, $"{documentNumber}.pdf");
        await exporter.ExportToPdfFileAsync(rows, template, filePath);
        return filePath;
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    // Buyer address = consumer's own address, falling back to their default delivery-book entry.
    // The invoice query's Include chain only reaches PrimaryAddress, so the book fallback is a
    // separate lookup, only run when there's no primary address to show.
    private static async Task<Address?> BuyerAddressAsync(FurniturePlannerContext db, Consumer? consumer, CancellationToken ct)
    {
        if (consumer is null) { return null; }
        if (consumer.PrimaryAddress is not null) { return consumer.PrimaryAddress; }
        var defaultBookEntry = await db.ConsumerDeliveryAddresses.AsNoTracking()
            .Include(d => d.Address)!.ThenInclude(a => a!.Region)
            .FirstOrDefaultAsync(d => d.ConsumerId == consumer.Id && d.IsDefault, ct);
        return defaultBookEntry?.Address;
    }

    // Same resolution rule as UblExport's helper of the same name - small duplication between the
    // two adapters rather than a shared utility, matching the UblExport/InvoicePdf precedent.
    private static async Task<Firm?> IssuingFirmAsync(FurniturePlannerContext db, int? orderFirmId, CancellationToken ct)
    {
        var firm = orderFirmId is int firmId
            ? await db.Firms.AsNoTracking().Include(f => f.Address)!.ThenInclude(a => a!.Region)
                .FirstOrDefaultAsync(f => f.Id == firmId, ct)
            : null;
        return firm ?? await db.Firms.AsNoTracking().Include(f => f.Address)!.ThenInclude(a => a!.Region)
            .FirstOrDefaultAsync(f => f.IsDefault, ct);
    }
}
