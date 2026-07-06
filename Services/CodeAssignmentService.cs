using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public sealed class TemplateFrozenException(string modelCode) : Exception($"Model '{modelCode}' is not in Draft; codes are frozen.");

public sealed class CodeAssignmentService(FurniturePlannerContext db)
{
    public async Task RegisterVariantAsync(string modelCode, string variantCode, CancellationToken ct = default)
    {
        await EnsureModelStateAsync(modelCode, ct);
        var exists = await db.VariantCodeTemplates.AnyAsync(t => t.ModelCode == modelCode && t.VariantCode == variantCode, ct);
        if (exists) { return; }
        var now = DateTime.UtcNow;
        db.VariantCodeTemplates.Add(new VariantCodeTemplate { ModelCode = modelCode, VariantCode = variantCode, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<VariantCodeTemplate>> GetForModelAsync(string modelCode, CancellationToken ct = default) =>
        await db.VariantCodeTemplates.Where(t => t.ModelCode == modelCode).OrderBy(t => t.VariantCode).AsNoTracking().ToListAsync(ct);

    public async Task<TradeItemState> GetModelStateAsync(string modelCode, CancellationToken ct = default) =>
        (await db.ModelStates.AsNoTracking().FirstOrDefaultAsync(s => s.ModelCode == modelCode, ct))?.State ?? TradeItemState.Draft;

    public async Task AssignAsync(int templateId, string? code, string? notes, CancellationToken ct = default)
    {
        var template = await db.VariantCodeTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found.");
        if (await GetModelStateAsync(template.ModelCode, ct) != TradeItemState.Draft)
        {
            throw new TemplateFrozenException(template.ModelCode);
        }
        template.SuggestedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        template.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        template.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> SuggestionsForModelAsync(string modelCode, CancellationToken ct = default) =>
        await db.VariantCodeTemplates
            .Where(t => t.ModelCode == modelCode && t.SuggestedCode != null)
            .AsNoTracking()
            .ToDictionaryAsync(t => t.VariantCode, t => t.SuggestedCode!, ct);

    public Task ReleaseModelAsync(string modelCode, CancellationToken ct = default) => TransitionAsync(modelCode, TradeItemState.Draft, TradeItemState.Active, ct);
    public Task DiscontinueModelAsync(string modelCode, CancellationToken ct = default) => TransitionAsync(modelCode, TradeItemState.Active, TradeItemState.Discontinued, ct);

    private async Task TransitionAsync(string modelCode, TradeItemState from, TradeItemState to, CancellationToken ct)
    {
        var row = await EnsureModelStateAsync(modelCode, ct);
        if (row.State != from) { throw new InvalidOperationException($"Model '{modelCode}' is {row.State}; cannot transition to {to}."); }
        row.State = to;
        await db.SaveChangesAsync(ct);
    }

    private async Task<ModelStateRecord> EnsureModelStateAsync(string modelCode, CancellationToken ct)
    {
        var row = await db.ModelStates.FirstOrDefaultAsync(s => s.ModelCode == modelCode, ct);
        if (row is null)
        {
            row = new ModelStateRecord { ModelCode = modelCode, State = TradeItemState.Draft };
            db.ModelStates.Add(row);
            await db.SaveChangesAsync(ct);
        }
        return row;
    }
}
