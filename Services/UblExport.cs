using System.Globalization;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapHelpers.Models.Dtos.Ubl;
using CheapHelpers.Services.DataExchange.Ubl;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// UBL accounting export through the CheapHelpers UBL layer (UblSharp underneath) - no hand-built
// XML in the planner. Values are the stored invoice/credit-note snapshots, the same rows the PDF
// reads, so the two documents can never disagree. ExportedAt flips only after a successful write.
public sealed class UblExport(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser, string outputRoot)
{
    // Full Peppol BIS validation is deferred - our parties carry no endpoint/tax scheme ids yet,
    // and the library's strict validator would reject every document.
    private readonly UblInvoiceService _ublService = new(new UblDocumentOptions { ValidateOnCreate = false });

    public async Task<string> ExportInvoiceAsync(int invoiceId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var invoice = await db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Order)!.ThenInclude(o => o!.Seller)!.ThenInclude(s => s!.Address)!.ThenInclude(a => a!.Region)
            .Include(i => i.Order)!.ThenInclude(o => o!.Consumer)!.ThenInclude(c => c!.PrimaryAddress)!.ThenInclude(a => a!.Region)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");

        var buyerAddress = await BuyerAddressAsync(db, invoice.Order?.Consumer, ct);
        var filePath = Path.Combine(outputRoot, $"{invoice.InvoiceNumber}.xml");
        await _ublService.CreateInvoiceAsync(MapInvoice(invoice, buyerAddress), filePath);
        invoice.ExportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return filePath;
    }

    public async Task<string> ExportCreditNoteAsync(int creditNoteId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var creditNote = await db.CreditNotes.FirstOrDefaultAsync(c => c.Id == creditNoteId, ct)
            ?? throw new InvalidOperationException($"Credit note {creditNoteId} not found.");
        var invoice = await db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Order)!.ThenInclude(o => o!.Seller)!.ThenInclude(s => s!.Address)!.ThenInclude(a => a!.Region)
            .Include(i => i.Order)!.ThenInclude(o => o!.Consumer)!.ThenInclude(c => c!.PrimaryAddress)!.ThenInclude(a => a!.Region)
            .FirstAsync(i => i.Id == creditNote.InvoiceId, ct);

        var buyerAddress = await BuyerAddressAsync(db, invoice.Order?.Consumer, ct);
        var filePath = Path.Combine(outputRoot, $"{creditNote.CreditNoteNumber}.xml");
        await _ublService.CreateCreditNoteAsync(MapCreditNote(creditNote, invoice, buyerAddress), filePath);
        creditNote.ExportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return filePath;
    }

    public async Task<List<string>> ExportNewAsync(CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        List<int> invoiceIds;
        List<int> creditNoteIds;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            invoiceIds = await db.Invoices.Where(i => i.ExportedAt == null).OrderBy(i => i.Id).Select(i => i.Id).ToListAsync(ct);
            creditNoteIds = await db.CreditNotes.Where(c => c.ExportedAt == null).OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(ct);
        }
        List<string> writtenPaths = [];
        foreach (var invoiceId in invoiceIds)
        {
            writtenPaths.Add(await ExportInvoiceAsync(invoiceId, ct));
        }
        foreach (var creditNoteId in creditNoteIds)
        {
            writtenPaths.Add(await ExportCreditNoteAsync(creditNoteId, ct));
        }
        return writtenPaths;
    }

    private static UblInvoice MapInvoice(Invoice invoice, Address? buyerAddress)
    {
        // Per-line nets are rounded independently, but the header net (invoice.NetTotal) rounds
        // the sum once - on multi-line invoices those two roundings can land a cent apart. The
        // invariant that must hold is: line totals must sum to the header net. Fold the
        // difference into the last line (every invoice has at least one) so the document is
        // internally consistent.
        var roundedLineNets = invoice.Lines
            .Select(line => Math.Round(line.LineTotal * (1 - invoice.OrderDiscountPercent / 100m), 2, MidpointRounding.AwayFromZero))
            .ToList();
        var roundingResidue = invoice.NetTotal - roundedLineNets.Sum();
        if (roundingResidue != 0m)
        {
            roundedLineNets[^1] += roundingResidue;
        }

        return new UblInvoice
        {
            Id = invoice.InvoiceNumber,
            IssueDate = invoice.IssuedAt,
            DueDate = invoice.DueDate,
            Seller = new UblParty { Name = invoice.Order?.Seller?.Name ?? "", Address = PartyAddress(invoice.Order?.Seller?.Address) },
            Buyer = new UblParty { Name = invoice.Order?.Consumer?.Name ?? "", TaxId = invoice.Order?.Consumer?.VatNumber, Address = PartyAddress(buyerAddress) },
            TaxTotal = new UblTaxTotal
            {
                TaxAmount = invoice.VatTotal,
                TaxSubtotals =
                [
                    new UblTaxSubtotal
                    {
                        TaxableAmount = invoice.NetTotal,
                        TaxAmount = invoice.VatTotal,
                        TaxCategory = VatCategory(invoice.Lines.FirstOrDefault()?.VatRatePercent ?? 0m),
                    },
                ],
            },
            Totals = new UblMonetaryTotals { LineExtensionAmount = invoice.NetTotal, TaxAmount = invoice.VatTotal, PayableAmount = invoice.GrossTotal },
            Lines = invoice.Lines.Select((line, index) => new UblInvoiceLine
            {
                Id = (index + 1).ToString(CultureInfo.InvariantCulture),
                Item = new UblItem { Name = line.Description },
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = roundedLineNets[index],
                TaxCategory = VatCategory(line.VatRatePercent),
            }).ToList(),
        };
    }

    private static UblCreditNote MapCreditNote(CreditNote creditNote, Invoice invoice, Address? buyerAddress) => new()
    {
        Id = creditNote.CreditNoteNumber,
        IssueDate = creditNote.IssuedAt,
        Note = creditNote.Note,
        CreditReason = creditNote.Reason.ToString(),
        BillingReferences = [invoice.InvoiceNumber],
        Seller = new UblParty { Name = invoice.Order?.Seller?.Name ?? "", Address = PartyAddress(invoice.Order?.Seller?.Address) },
        Buyer = new UblParty { Name = invoice.Order?.Consumer?.Name ?? "", TaxId = invoice.Order?.Consumer?.VatNumber, Address = PartyAddress(buyerAddress) },
        TaxTotal = new UblTaxTotal
        {
            TaxAmount = creditNote.VatAmount,
            TaxSubtotals =
            [
                new UblTaxSubtotal
                {
                    TaxableAmount = creditNote.NetAmount,
                    TaxAmount = creditNote.VatAmount,
                    TaxCategory = VatCategory(invoice.Lines.FirstOrDefault()?.VatRatePercent ?? 0m),
                },
            ],
        },
        Totals = new UblMonetaryTotals { LineExtensionAmount = creditNote.NetAmount, TaxAmount = creditNote.VatAmount, PayableAmount = creditNote.GrossAmount },
        Lines =
        [
            new UblInvoiceLine
            {
                Id = "1",
                Item = new UblItem { Name = $"Credit - {creditNote.Reason}" },
                Quantity = 1,
                UnitPrice = creditNote.NetAmount,
                LineTotal = creditNote.NetAmount,
                TaxCategory = VatCategory(invoice.Lines.FirstOrDefault()?.VatRatePercent ?? 0m),
            },
        ],
    };

    private static UblTaxCategory VatCategory(decimal ratePercent) => new() { Id = ratePercent == 0m ? "Z" : "S", Percent = ratePercent };

    private static UblAddress? PartyAddress(Address? address) => address is null ? null : new UblAddress
    {
        StreetName = address.Street,
        BuildingNumber = address.Number,
        PostBox = address.Box,
        CityName = address.City,
        PostalZone = address.PostalCode,
        CountryCode = address.CountryCode,
        CountrySubentity = address.Region?.Name,
    };

    // Buyer address = consumer's own address, falling back to their default delivery-book entry -
    // same rule as InvoicePdf. The invoice query's Include chain only reaches PrimaryAddress, so
    // the book fallback is a separate lookup, only run when there's no primary address to show.
    private static async Task<Address?> BuyerAddressAsync(FurniturePlannerContext db, Consumer? consumer, CancellationToken ct)
    {
        if (consumer is null) { return null; }
        if (consumer.PrimaryAddress is not null) { return consumer.PrimaryAddress; }
        var defaultBookEntry = await db.ConsumerDeliveryAddresses.AsNoTracking()
            .Include(d => d.Address)!.ThenInclude(a => a!.Region)
            .FirstOrDefaultAsync(d => d.ConsumerId == consumer.Id && d.IsDefault, ct);
        return defaultBookEntry?.Address;
    }

    private async Task RequireAdminOrOfficeAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office)) { return; }
        throw new InvalidOperationException("Only Admin or Office can do this.");
    }
}
