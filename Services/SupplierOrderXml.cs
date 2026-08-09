using System.Globalization;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapHelpers.Models.Dtos.Ubl;
using CheapHelpers.Services.DataExchange.Ubl;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// UBL order export through the CheapHelpers UBL layer, same shell as UblExport - but a purchase
// order is an internal document to our own supplier, not a Peppol-network exchange, so there is no
// validation gate to configure (unlike UblInvoiceService's ValidateOnCreate: false).
public sealed class SupplierOrderXml(IDbContextFactory<FurniturePlannerContext> factory, string outputRoot)
{
    private readonly UblService _ublService = new(new UblDocumentOptions());

    public async Task<string> GenerateAsync(int supplierOrderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.SupplierOrders.AsNoTracking()
            .Include(o => o.Supplier)!.ThenInclude(s => s!.Address)!.ThenInclude(a => a!.Region)
            .Include(o => o.Units)
            .FirstOrDefaultAsync(o => o.Id == supplierOrderId, ct)
            ?? throw new InvalidOperationException($"Purchase order {supplierOrderId} not found.");

        var lineIds = order.Units.Select(u => u.OrderLineId).Distinct().ToList();
        var lines = await db.OrderLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        var firm = await db.Firms.AsNoTracking()
            .Include(f => f.Address)!.ThenInclude(a => a!.Region)
            .FirstOrDefaultAsync(f => f.IsDefault, ct);

        var groups = order.Units
            .GroupBy(unit => Identity(lines[unit.OrderLineId]))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        var ublOrder = new UblOrder
        {
            Id = order.PoNumber,
            IssueDate = order.SentAt ?? order.CreatedAt,
            // Buyer = us: the default firm when one is configured (per-firm PO routing is a
            // deliberate deferral - POs span orders across collections). The neutral constant
            // remains only as the unconfigured-install fallback.
            Buyer = firm is null
                ? new UblParty { Name = "CheapFurniturePlanner" }
                : new UblParty { Name = firm.Name, TaxId = firm.VatNumber, EndpointId = firm.PeppolEndpointId, Address = PartyAddress(firm.Address) },
            Seller = new UblParty { Name = order.Supplier?.Name ?? "", Address = PartyAddress(order.Supplier?.Address) },
            Lines = groups.Select((group, index) => new UblOrderLine
            {
                Id = (index + 1).ToString(CultureInfo.InvariantCulture),
                Item = new UblItem { Name = group.Key },
                Quantity = group.Count(),
                // Prices are not part of this PO phase - purchasing cost is negotiated separately.
                UnitPrice = 0m,
                LineTotal = 0m,
            }).ToList(),
        };

        Directory.CreateDirectory(outputRoot);
        var filePath = Path.Combine(outputRoot, $"{order.PoNumber}-order.xml");
        await _ublService.CreateOrderAsync(ublOrder, filePath);
        return filePath;
    }

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

    // Same identity rule as SupplierOrderPdf's private helper of the same name - see that file's
    // comment. Small duplication between the two adapters rather than a shared utility, matching
    // the UblExport/InvoicePdf and TripLoadListPdf pairs.
    private static string Identity(OrderLine line)
    {
        var basePart = line.Kind == OrderLineKind.ConfiguredElement
            ? string.Join("/", new[] { line.ModelCode, line.ElementCode, line.VariantCode }.Where(part => !string.IsNullOrEmpty(part)))
            : line.AssignedCode ?? "";
        return string.IsNullOrEmpty(line.FabricColorCode) ? basePart : $"{basePart} / {line.FabricColorCode}";
    }
}
