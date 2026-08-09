using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapHelpers.Services.DataExchange.Pdf;
using CheapHelpers.Services.DataExchange.Pdf.Configuration;
using CheapHelpers.Services.DataExchange.Pdf.Templates;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Renders a purchase order to a PDF for the supplier. Rendering only - Draft/Sent lifecycle and
// unit assignment live in PurchasingService, so a failed render never mutates PO state.
public sealed class SupplierOrderPdf(IDbContextFactory<FurniturePlannerContext> factory, IPdfExportService exporter, string outputRoot)
{
    private sealed record DocumentRow(string Label, string Detail);

    public async Task<string> GenerateAsync(int supplierOrderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.SupplierOrders.AsNoTracking()
            .Include(o => o.Supplier)!.ThenInclude(s => s!.Address)
            .Include(o => o.Units)
            .FirstOrDefaultAsync(o => o.Id == supplierOrderId, ct)
            ?? throw new InvalidOperationException($"Purchase order {supplierOrderId} not found.");

        var lineIds = order.Units.Select(u => u.OrderLineId).Distinct().ToList();
        var lines = await db.OrderLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

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
        rows.Add(new("PO number", order.PoNumber));
        rows.Add(new("Created", order.CreatedAt.ToString("yyyy-MM-dd")));
        if (order.SentAt is not null) { rows.Add(new("Sent", order.SentAt.Value.ToString("yyyy-MM-dd"))); }
        if (!string.IsNullOrWhiteSpace(order.TheirReference)) { rows.Add(new("Their reference", order.TheirReference)); }
        rows.Add(new("Units", order.Units.Count.ToString()));

        rows.AddRange(order.Units
            .GroupBy(unit => Identity(lines[unit.OrderLineId]))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new DocumentRow(group.Key, group.Count().ToString())));

        // Header/footer off: the library header prints literal company placeholders. IsBold off
        // everywhere: the packaged renderer duplicates bold cell text.
        var template = new PdfDocumentTemplate
        {
            Title = $"Purchase order {order.PoNumber}",
            UseHeader = false,
            UseFooter = false,
            Columns =
            [
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Label), DisplayName = "Field", Width = 3f, FontSize = 10 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Detail), DisplayName = "Detail", Width = 1f, FontSize = 10 },
            ],
        };

        Directory.CreateDirectory(outputRoot);
        var filePath = Path.Combine(outputRoot, $"{order.PoNumber}.pdf");
        await exporter.ExportToPdfFileAsync(rows, template, filePath);
        return filePath;
    }

    // Configured lines identify by Model/Element/Variant (join skips any missing part); standalone
    // lines by their assigned article code. FabricColorCode, when set, is appended - two units of
    // the same variant in different fabrics are not the same purchasable identity.
    private static string Identity(OrderLine line)
    {
        var basePart = line.Kind == OrderLineKind.ConfiguredElement
            ? string.Join("/", new[] { line.ModelCode, line.ElementCode, line.VariantCode }.Where(part => !string.IsNullOrEmpty(part)))
            : line.AssignedCode ?? "";
        return string.IsNullOrEmpty(line.FabricColorCode) ? basePart : $"{basePart} / {line.FabricColorCode}";
    }
}
