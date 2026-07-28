using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapHelpers.Services.DataExchange.Pdf;
using CheapHelpers.Services.DataExchange.Pdf.Configuration;
using CheapHelpers.Services.DataExchange.Pdf.Templates;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Renders a trip's load list to a PDF the dock hands to the driver. Any trip state may generate
// (Planning = provisional, Departed = final, Completed = record copy) - only a missing trip
// throws. Rendering only, no state mutation.
public sealed class TripLoadListPdf(IDbContextFactory<FurniturePlannerContext> factory, IPdfExportService exporter, string outputRoot)
{
    private sealed record DocumentRow(string Position, string UnitCode, string OrderNumber, string Consumer, string Delivery, string Promise);

    public async Task<string> GenerateAsync(int tripId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var trip = await db.Trips.AsNoTracking()
            .Include(t => t.Units).ThenInclude(u => u.Order)!.ThenInclude(o => o!.Consumer)
            .Include(t => t.Units).ThenInclude(u => u.Order)!.ThenInclude(o => o!.DeliveryAddress)
            .Include(t => t.Region)
            .FirstOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new InvalidOperationException($"Trip {tripId} not found.");

        List<DocumentRow> rows =
        [
            new("Departure", trip.DepartureDate?.ToString("yyyy-MM-dd") ?? "", "", "", "", ""),
            new("Truck", trip.TruckName ?? "", "", "", "", ""),
            new("Driver", trip.DriverName ?? "", "", "", "", ""),
            new("Region", trip.Region?.Name ?? "", "", "", "", ""),
            new("Units", trip.Units.Count.ToString(), "", "", "", ""),
        ];
        rows.AddRange(trip.Units
            .OrderBy(u => u.LoadPosition.HasValue ? 0 : 1).ThenBy(u => u.LoadPosition).ThenBy(u => u.UnitCode)
            .Select(unit =>
            {
                var order = unit.Order;
                var promised = order?.PromisedDeliveryDate;
                var promiseText = promised?.ToString("yyyy-MM-dd") ?? "";
                if (ProductionUnitService.PromiseMissed(promised, trip.DepartureDate)) { promiseText += " !"; }
                return new DocumentRow(
                    unit.LoadPosition?.ToString() ?? "",
                    unit.UnitCode + (unit.State == ProductionUnitState.Expected ? " (expected)" : ""),
                    order?.OrderNumber ?? "",
                    order?.Consumer?.Name ?? "",
                    order?.DeliveryAddress?.ToOneLine() ?? "",
                    promiseText);
            }));

        // Header/footer off: the library header prints literal company placeholders. IsBold off
        // everywhere: the packaged renderer duplicates bold cell text.
        var template = new PdfDocumentTemplate
        {
            Title = $"Load list {trip.TripCode}",
            UseHeader = false,
            UseFooter = false,
            Columns =
            [
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Position), DisplayName = "Pos", Width = 1f, FontSize = 9 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.UnitCode), DisplayName = "Unit", Width = 2f, FontSize = 9 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.OrderNumber), DisplayName = "Order", Width = 2f, FontSize = 9 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Consumer), DisplayName = "Consumer", Width = 2f, FontSize = 9 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Delivery), DisplayName = "Delivery", Width = 3f, FontSize = 9 },
                new PdfColumnConfig { PropertyName = nameof(DocumentRow.Promise), DisplayName = "Promise", Width = 2f, FontSize = 9 },
            ],
        };

        Directory.CreateDirectory(outputRoot);
        var filePath = Path.Combine(outputRoot, $"{trip.TripCode}-loadlist.pdf");
        await exporter.ExportToPdfFileAsync(rows, template, filePath);
        return filePath;
    }
}
