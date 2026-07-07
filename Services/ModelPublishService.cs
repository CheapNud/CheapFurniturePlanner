using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public record AuthoringModel(string Code, string Name, TradeItemState State);

public sealed class ModelPublishService(IDbContextFactory<FurniturePlannerContext> factory, CataloguePublishService publish, ICatalogueSource source)
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

    // Loads the embedded authoring seed, keeps only the models whose state row is Active, and
    // publishes that Active-only snapshot. PublishAsync validates, persists, flips IsCurrent and
    // invalidates the source, so the published catalogue the planner reads only ever contains
    // released models.
    public async Task RepublishAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var active = (await db.ModelStates.AsNoTracking().Where(s => s.State == TradeItemState.Active).Select(s => s.ModelCode).ToListAsync(ct)).ToHashSet();
        var snapshot = SeedCatalogue.Load();
        snapshot.Models = snapshot.Models.Where(m => active.Contains(m.Code)).ToList();
        var result = await publish.PublishAsync(snapshot);
        if (!result.Success)
        {
            throw new InvalidOperationException("Republish failed validation: " + string.Join("; ", result.Errors));
        }
    }

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

        // Keep the transition atomic with a valid publish: a model that would break the Active-only
        // snapshot must not be releasable. If the republish fails, revert the state row to its prior
        // value and rethrow so the transition does not stand.
        try
        {
            await RepublishAsync(ct);
        }
        catch
        {
            row.State = from;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }
}
