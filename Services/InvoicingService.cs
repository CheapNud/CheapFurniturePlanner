using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Invoicing lives entirely on the order's stored money: lines are copied field-for-field at
// issue time and never re-derived from masters (the legacy exporter re-read current customer
// discounts and summed them flat - both bugs are structurally impossible here). Issued
// documents are immutable; only the paid/settled flags ever change.
public sealed class InvoicingService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser)
{
    public async Task<List<MarketVatRate>> VatRatesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MarketVatRates.AsNoTracking().OrderBy(r => r.MarketCode).ToListAsync(ct);
    }

    public async Task SetVatRateAsync(string marketCode, decimal ratePercent, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var trimmedMarket = (marketCode ?? "").Trim();
        if (trimmedMarket.Length == 0) { throw new InvalidOperationException("Market code is required."); }
        if (ratePercent is < 0 or > 100) { throw new InvalidOperationException("VAT rate must be between 0 and 100."); }
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.MarketVatRates.FirstOrDefaultAsync(r => r.MarketCode == trimmedMarket, ct);
        if (existing is null)
        {
            db.MarketVatRates.Add(new MarketVatRate { MarketCode = trimmedMarket, RatePercent = ratePercent });
        }
        else
        {
            existing.RatePercent = ratePercent;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteVatRateAsync(int vatRateId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var rate = await db.MarketVatRates.FirstOrDefaultAsync(r => r.Id == vatRateId, ct)
            ?? throw new InvalidOperationException($"VAT rate {vatRateId} not found.");
        db.MarketVatRates.Remove(rate);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Invoice> CreateInvoiceAsync(int orderId, DateTime? dueDate = null, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.Orders.Include(o => o.Lines.OrderBy(l => l.DisplayIndex))
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        if (order.State != OrderState.Placed) { throw new InvalidOperationException($"Order {order.OrderNumber} is not placed."); }
        if (await db.Invoices.AnyAsync(i => i.OrderId == orderId, ct)) { throw new InvalidOperationException($"Order {order.OrderNumber} is already invoiced."); }
        var vatRate = await db.MarketVatRates.FirstOrDefaultAsync(r => r.MarketCode == order.MarketCode, ct)
            ?? throw new InvalidOperationException($"No VAT rate configured for market '{order.MarketCode}'.");

        var prefix = $"INV-{DateTime.UtcNow.Year}-";
        var countThisYear = await db.Invoices.CountAsync(i => i.InvoiceNumber.StartsWith(prefix), ct);
        var issuedAt = DateTime.UtcNow;
        var invoice = new Invoice
        {
            InvoiceNumber = $"{prefix}{countThisYear + 1:D4}",
            OrderId = order.Id,
            IssuedAt = issuedAt,
            DueDate = dueDate ?? issuedAt.AddDays(30),
            OrderDiscountPercent = order.OrderDiscountPercent,
            CreatedByUserId = await RequireUserIdAsync(),
        };
        decimal netSum = 0m;
        decimal vatSum = 0m;
        foreach (var line in order.Lines)
        {
            var lineNet = line.LineTotal * (1 - order.OrderDiscountPercent / 100m);
            var vatAmount = Math.Round(lineNet * vatRate.RatePercent / 100m, 2, MidpointRounding.AwayFromZero);
            netSum += lineNet;
            vatSum += vatAmount;
            invoice.Lines.Add(new InvoiceLine
            {
                OrderLineId = line.Id,
                Description = LineDescription(line),
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                LineTotal = line.LineTotal,
                VatRatePercent = vatRate.RatePercent,
                VatAmount = vatAmount,
            });
        }
        invoice.NetTotal = Math.Round(netSum, 2, MidpointRounding.AwayFromZero);
        invoice.VatTotal = vatSum;
        invoice.GrossTotal = invoice.NetTotal + invoice.VatTotal;
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    public async Task<Invoice?> InvoiceForOrderAsync(int orderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.OrderId == orderId, ct);
    }

    public async Task<List<Invoice>> ListInvoicesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking()
            .Include(i => i.Order)!.ThenInclude(o => o!.Consumer)
            .Include(i => i.CreditNotes)
            .OrderByDescending(i => i.InvoiceNumber)
            .ToListAsync(ct);
    }

    public async Task<Invoice?> GetInvoiceAsync(int invoiceId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.CreditNotes)
            .Include(i => i.Order)!.ThenInclude(o => o!.Consumer)
            .Include(i => i.Order)!.ThenInclude(o => o!.Seller)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
    }

    public async Task MarkPaidAsync(int invoiceId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");
        if (invoice.IsPaid) { throw new InvalidOperationException($"Invoice {invoice.InvoiceNumber} is already paid."); }
        invoice.IsPaid = true;
        invoice.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ----- shared internals (Task 3 appends credit-note methods above this region) -----

    private static string LineDescription(OrderLine line)
    {
        var identity = line.AssignedCode ?? line.VariantCode ?? $"article {line.ArticleId}";
        return line.FabricColorCode is null ? identity : $"{identity} / {line.FabricColorCode}";
    }

    private async Task RequireAdminOrOfficeAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office)) { return; }
        throw new InvalidOperationException("Only Admin or Office can do this.");
    }

    private async Task<string> RequireUserIdAsync() =>
        await currentUser.UserIdAsync() ?? throw new InvalidOperationException("No signed-in user.");
}
