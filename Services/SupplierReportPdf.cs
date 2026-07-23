using CheapFurniturePlanner.Data;
using CheapHelpers.Services.DataExchange.Pdf;
using CheapHelpers.Services.DataExchange.Pdf.Configuration;
using CheapHelpers.Services.DataExchange.Pdf.Templates;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Renders the external-flow supplier report to a PDF file through the CheapHelpers PDF stack.
// Rendering only - stamping ReportedAt and the state transition live in ServiceTicketService
// (MarkReportedAsync), so a failed render never mutates ticket state.
public sealed class SupplierReportPdf(IDbContextFactory<FurniturePlannerContext> factory, IPdfExportService exporter, string outputRoot)
{
    private sealed record ReportRow(string Label, string Text);

    public async Task<string> GenerateAsync(int ticketId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ticket = await db.ServiceTickets.AsNoTracking()
            .Include(t => t.Consumer).Include(t => t.Lines).Include(t => t.Photos).Include(t => t.SupplierReport)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new InvalidOperationException($"Ticket {ticketId} not found.");
        var report = ticket.SupplierReport ?? throw new InvalidOperationException($"Ticket {ticket.TicketNumber} has no supplier report flow.");

        List<ReportRow> rows =
        [
            new("Ticket", ticket.TicketNumber),
            new("Consumer", ticket.Consumer?.Name ?? ""),
            new("Supplier reference", report.SupplierRef),
            new("Problem", ticket.ProblemDescription),
        ];
        if (!string.IsNullOrWhiteSpace(ticket.VisitAddress)) { rows.Add(new("Address", ticket.VisitAddress)); }
        rows.AddRange(ticket.Lines.Select((line, index) => new ReportRow($"Item {index + 1}", line.Description)));
        rows.AddRange(ticket.Photos.Select((photo, index) => new ReportRow($"Photo {index + 1}", $"{photo.Kind}: {photo.FileName}")));

        // Header/footer off: the library header prints literal company placeholders. IsBold off
        // everywhere: the packaged renderer adds bold cell text twice.
        var template = new PdfDocumentTemplate
        {
            Title = $"Service report {ticket.TicketNumber}",
            UseHeader = false,
            UseFooter = false,
            Columns =
            [
                new PdfColumnConfig { PropertyName = nameof(ReportRow.Label), DisplayName = "Field", Width = 1f, FontSize = 10 },
                new PdfColumnConfig { PropertyName = nameof(ReportRow.Text), DisplayName = "Detail", Width = 3f, FontSize = 10 },
            ],
        };

        Directory.CreateDirectory(outputRoot);
        var filePath = Path.Combine(outputRoot, $"{ticket.TicketNumber}.pdf");
        await exporter.ExportToPdfFileAsync(rows, template, filePath);
        return filePath;
    }
}
