using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public sealed class ModelActiveException(string modelCode)
    : Exception($"Model '{modelCode}' is Active; set it out of Active before deleting.");

public sealed class ModelAuthoringService(IDbContextFactory<FurniturePlannerContext> factory, AuthoringCatalogueStore store, ModelPublishService publish, ArticleAuthoringService articles)
{
    public async Task CreateBlankAsync(string code, string name, string? collectionCode, string? modelTypeCode, CancellationToken ct = default)
    {
        await EnsureCodeAvailableAsync(code, ct);
        await store.SaveModelAsync(new FurnitureModel { Code = code, Name = name, CollectionCode = collectionCode, ModelTypeCode = modelTypeCode }, ct);
    }

    public async Task CreateFromCloneAsync(string sourceCode, string newCode, string newName, CancellationToken ct = default)
    {
        await EnsureCodeAvailableAsync(newCode, ct);
        var src = await store.LoadModelAsync(sourceCode, ct)
            ?? throw new InvalidOperationException($"Source model '{sourceCode}' not found.");
        var clone = CanonicalJson.Deserialize<FurnitureModel>(CanonicalJson.Serialize(src))
            ?? throw new InvalidOperationException("Failed to clone model.");
        clone.Code = newCode;
        clone.Name = newName;
        await store.SaveModelAsync(clone, ct);
    }

    public async Task RenameAsync(string code, string name, string? collectionCode, string? modelTypeCode, CancellationToken ct = default)
    {
        var model = await store.LoadModelAsync(code, ct)
            ?? throw new InvalidOperationException($"Model '{code}' not found.");
        var priorName = model.Name;
        var priorCollectionCode = model.CollectionCode;
        var priorModelTypeCode = model.ModelTypeCode;
        model.Name = name;
        model.CollectionCode = collectionCode;
        model.ModelTypeCode = modelTypeCode;
        await store.SaveModelAsync(model, ct);
        if (await publish.GetStateAsync(code, ct) == TradeItemState.Active)
        {
            try
            {
                await publish.RepublishAsync(ct: ct);
            }
            catch
            {
                model.Name = priorName;
                model.CollectionCode = priorCollectionCode;
                model.ModelTypeCode = priorModelTypeCode;
                await store.SaveModelAsync(model, ct);
                throw;
            }
        }
    }

    public async Task DeleteAsync(string code, CancellationToken ct = default)
    {
        if (await publish.GetStateAsync(code, ct) == TradeItemState.Active)
        {
            throw new ModelActiveException(code);
        }
        // The doc row is deleted here (not via store.DeleteModelAsync) so it can share the same
        // context/transaction as the dependent-row delete below, keeping both atomic.
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.ModelStates.Where(s => s.ModelCode == code).ExecuteDeleteAsync(ct);
        await db.AuthoringModels.Where(m => m.ModelCode == code).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        // The articles document is a separate write (own context, outside this transaction) - a
        // known two-context window, same benign single-user follow-up as the other authoring
        // services that call through the store rather than sharing this transaction.
        await articles.DeleteForModelAsync(code, ct);
    }

    private async Task EnsureCodeAvailableAsync(string code, CancellationToken ct)
    {
        if ((await store.ModelCodesAsync(ct)).Contains(code))
        {
            throw new InvalidOperationException($"Model code '{code}' already exists.");
        }
    }
}
