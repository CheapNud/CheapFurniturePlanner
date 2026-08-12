using System.Globalization;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapHelpers.Services.DataExchange.Pdf;
using CheapHelpers.Services.DataExchange.Pdf.Configuration;
using CheapHelpers.Services.DataExchange.Pdf.Templates;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Renders a material order to a PDF for the supplier. Rendering only - Draft/Sent lifecycle and
// line edits live in MaterialOrderService, so a failed render never mutates order state. Skeleton
// copies SupplierOrderPdf's, including the FI-1 default-firm "Ordered by" block.
public sealed class MaterialOrderPdf(IDbContextFactory<FurniturePlannerContext> factory, IPdfExportService exporter, string outputRoot)
{
    private sealed record DocumentRow(string Label, string Detail);

    public async Task<string> GenerateAsync(int materialOrderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.MaterialOrders.AsNoTracking()
            .Include(o => o.Supplier)!.ThenInclude(s => s!.Address)
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == materialOrderId, ct)
            ?? throw new InvalidOperationException($"Material order {materialOrderId} not found.");

        var firm = await db.Firms.AsNoTracking()
            .Include(f => f.Address)!.ThenInclude(a => a!.Region)
            .FirstOrDefaultAsync(f => f.IsDefault, ct);

        List<DocumentRow> rows = [];
        if (firm is not null)
        {
            rows.Add(new("Ordered by", string.IsNullOrWhiteSpace(firm.VatNumber) ? firm.Name : $"{firm.Name} — VAT {firm.VatNumber}"));
            if (firm.Address is not null) { rows.Add(new("Our address", firm.Address.ToOneLine())); }
        }
        rows.Add(new("Supplier", order.Supplier?.Name ?? ""));
        if (order.Supplier?.Address is not null) { rows.Add(new("Supplier address", order.Supplier.Address.ToOneLine())); }
        rows.Add(new("Order number", order.Number));
        rows.Add(new("Created", order.CreatedAt.ToString("yyyy-MM-dd")));
        if (order.SentAt is not null) { rows.Add(new("Sent", order.SentAt.Value.ToString("yyyy-MM-dd"))); }
        if (!string.IsNullOrWhiteSpace(order.TheirReference)) { rows.Add(new("Their reference", order.TheirReference)); }
        rows.Add(new("Lines", order.Lines.Count.ToString()));

        rows.AddRange(order.Lines
            .OrderBy(line => line.Code, StringComparer.Ordinal)
            .Select(line => new DocumentRow(line.Code,
                $"{line.Kind}{(string.IsNullOrWhiteSpace(line.DisplayName) ? "" : $" — {line.DisplayName}")} — ordered {line.QuantityOrdered.ToString(CultureInfo.InvariantCulture)}")));

        // Header/footer off: the library header prints literal company placeholders. IsBold off
        // everywhere: the packaged renderer duplicates bold cell text.
        var template = new PdfDocumentTemplate
        {
            Title = $"Material order {order.Number}",
            UseHeader = false,
            UseFooter = false,
            Columns =
            [
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Label), DisplayName = "Field", Width = 3f, FontSize = 10 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Detail), DisplayName = "Detail", Width = 1f, FontSize = 10 },
            ],
        };

        Directory.CreateDirectory(outputRoot);
        var filePath = Path.Combine(outputRoot, $"{order.Number}.pdf");
        await exporter.ExportToPdfFileAsync(rows, template, filePath);
        return filePath;
    }
}
