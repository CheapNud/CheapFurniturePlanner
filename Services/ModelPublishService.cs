using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public record AuthoringModel(string Code, string Name, TradeItemState State);

public sealed class ModelPublishService(IDbContextFactory<FurniturePlannerContext> factory, CataloguePublishService publish, ICatalogueSource source, AuthoringCatalogueStore store)
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
        return (await store.LoadAsync(ct)).Models
            .Select(m => new AuthoringModel(m.Code, m.Name, states.TryGetValue(m.Code, out var st) ? st : TradeItemState.Draft))
            .OrderBy(m => m.Code)
            .ToList();
    }

    // Free-flow: set a model to any state at any time. Republishes the Active-only snapshot and, if that
    // fails validation (e.g. releasing a model that has no elements), reverts the state row and rethrows.
    public async Task SetStateAsync(string modelCode, TradeItemState state, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ModelStates.FirstOrDefaultAsync(s => s.ModelCode == modelCode, ct);
        var prior = row?.State ?? TradeItemState.Draft;
        if (row is null)
        {
            row = new ModelStateRecord { ModelCode = modelCode, State = state };
            db.ModelStates.Add(row);
        }
        else { row.State = state; }
        await db.SaveChangesAsync(ct);
        try
        {
            await RepublishAsync(ct);
        }
        catch
        {
            row.State = prior;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    // Loads the persisted authoring catalogue, keeps only the models whose state row is Active, and
    // publishes that Active-only snapshot. PublishAsync validates, persists, flips IsCurrent and
    // invalidates the source, so the published catalogue the planner reads only ever contains
    // released models.
    public async Task RepublishAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var active = (await db.ModelStates.AsNoTracking().Where(s => s.State == TradeItemState.Active).Select(s => s.ModelCode).ToListAsync(ct)).ToHashSet();
        var snapshot = await store.LoadAsync(ct);
        snapshot.Models = snapshot.Models.Where(m => active.Contains(m.Code)).ToList();
        var result = await publish.PublishAsync(snapshot);
        if (!result.Success)
        {
            throw new InvalidOperationException("Republish failed validation: " + string.Join("; ", result.Errors));
        }
    }
}
