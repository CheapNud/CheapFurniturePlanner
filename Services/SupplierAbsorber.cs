using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// One-time (idempotent) startup absorb of the legacy free-text supplier refs into the Supplier
// entity: every distinct ref becomes a Supplier row (code = name = the ref) unless a supplier
// with that code already exists, then the OrderLine/SupplierReport FKs are matched by code.
// The old string columns stay physically until the next phase's migration drops them; nothing
// outside this class reads them anymore.
public sealed class SupplierAbsorber(IDbContextFactory<FurniturePlannerContext> factory)
{
    public async Task AbsorbAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var lineRefs = await db.OrderLines.Where(l => l.SupplierRef != null && l.SupplierId == null).Select(l => l.SupplierRef!).ToListAsync(ct);
        var reportRefs = await db.SupplierReports.Where(r => r.SupplierRef != null && r.SupplierRef != "" && r.SupplierId == null).Select(r => r.SupplierRef!).ToListAsync(ct);
        var distinctRefs = lineRefs.Concat(reportRefs).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct().ToList();
        if (distinctRefs.Count == 0) { return; }

        var knownCodes = await db.Suppliers.Select(s => s.Code).ToListAsync(ct);
        foreach (var supplierRef in distinctRefs.Where(r => !knownCodes.Contains(r)))
        {
            db.Suppliers.Add(new Supplier { Code = supplierRef, Name = supplierRef });
        }
        await db.SaveChangesAsync(ct);

        var suppliersByCode = await db.Suppliers.ToDictionaryAsync(s => s.Code, s => s.Id, ct);
        foreach (var line in await db.OrderLines.Where(l => l.SupplierRef != null && l.SupplierId == null).ToListAsync(ct))
        {
            if (suppliersByCode.TryGetValue(line.SupplierRef!.Trim(), out var supplierId)) { line.SupplierId = supplierId; }
        }
        foreach (var report in await db.SupplierReports.Where(r => r.SupplierRef != null && r.SupplierRef != "" && r.SupplierId == null).ToListAsync(ct))
        {
            if (suppliersByCode.TryGetValue(report.SupplierRef!.Trim(), out var supplierId)) { report.SupplierId = supplierId; }
        }
        await db.SaveChangesAsync(ct);
    }
}
