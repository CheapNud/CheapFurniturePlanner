using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public record AuthoringModel(string Code, string Name, TradeItemState State);

public sealed class ModelPublishService(IDbContextFactory<FurniturePlannerContext> factory)
{
    public async Task<TradeItemState> GetStateAsync(string modelCode, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.ModelStates.AsNoTracking().FirstOrDefaultAsync(s => s.ModelCode == modelCode, ct))?.State ?? TradeItemState.Draft;
    }

    public async Task<IReadOnlyList<AuthoringModel>> GetAuthoringModelsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var states = await db.ModelStates.AsNoTracking().ToDictionaryAsync(s => s.ModelCode, s => s.State, ct);
        return SeedCatalogue.Load().Models
            .Select(m => new AuthoringModel(m.Code, m.Name, states.TryGetValue(m.Code, out var st) ? st : TradeItemState.Draft))
            .OrderBy(m => m.Code)
            .ToList();
    }

    public Task ReleaseAsync(string modelCode, CancellationToken ct = default) => TransitionAsync(modelCode, TradeItemState.Draft, TradeItemState.Active, ct);
    public Task DiscontinueAsync(string modelCode, CancellationToken ct = default) => TransitionAsync(modelCode, TradeItemState.Active, TradeItemState.Discontinued, ct);

    private async Task TransitionAsync(string modelCode, TradeItemState from, TradeItemState to, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ModelStates.FirstOrDefaultAsync(s => s.ModelCode == modelCode, ct);
        if (row is null)
        {
            row = new ModelStateRecord { ModelCode = modelCode, State = TradeItemState.Draft };
            db.ModelStates.Add(row);
        }
        if (row.State != from) { throw new InvalidOperationException($"Model '{modelCode}' is {row.State}; cannot transition to {to}."); }
        row.State = to;
        await db.SaveChangesAsync(ct);
        // Task 2 adds the RepublishAsync() call here.
    }
}
