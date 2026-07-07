using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public sealed class NamingFrozenException(string modelCode) : Exception($"Model '{modelCode}' is not in Draft; variant names are frozen.");

// The studio's sparse variant-naming registry: a row only ever exists for a variant that has
// actually been assigned a code (e.g. "STUDIO-A"), and edits are only permitted while the owning
// model is still in Draft (Phase 3's ModelPublishService gate) so a released model's names cannot
// drift.
public sealed class VariantNamingService(IDbContextFactory<FurniturePlannerContext> factory, ModelPublishService publish)
{
    public async Task<IReadOnlyDictionary<string, string>> NamesForModelAsync(string modelCode, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.VariantNamings.Where(n => n.ModelCode == modelCode).AsNoTracking()
            .ToDictionaryAsync(n => n.VariantCode, n => n.AssignedCode, ct);
    }

    public async Task AssignAsync(string modelCode, string variantCode, string? code, CancellationToken ct = default)
    {
        if (await publish.GetStateAsync(modelCode, ct) != TradeItemState.Draft)
        {
            throw new NamingFrozenException(modelCode);
        }
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.VariantNamings.FirstOrDefaultAsync(n => n.ModelCode == modelCode && n.VariantCode == variantCode, ct);
        var trimmed = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        if (trimmed is null)
        {
            if (row is not null) { db.VariantNamings.Remove(row); await db.SaveChangesAsync(ct); }
            return;
        }
        if (row is null)
        {
            var now = DateTime.UtcNow;
            db.VariantNamings.Add(new VariantNaming { ModelCode = modelCode, VariantCode = variantCode, AssignedCode = trimmed, CreatedAt = now, UpdatedAt = now });
        }
        else { row.AssignedCode = trimmed; row.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
    }
}
