using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Domain.Catalog;

namespace CheapFurniturePlanner.Services;

public sealed class NamingFrozenException(string modelCode) : Exception($"Model '{modelCode}' is not in Draft; variant names are frozen.");

// The article catalogue's authoring service — successor to VariantNamingService, absorbing its
// naming API: naming a variant now CREATES a catalogue-backed Article (assigned code + full
// provenance), still Draft-gated so a released model's articles cannot drift. Standalone articles
// (legacy/dropship: manual price, optional supplier ref, no provenance) get their own CRUD with a
// free-flow State. Prune/DeleteForModel are the cascade hooks the structure-authoring services call
// where they used to delete VariantNaming rows.
public sealed class ArticleAuthoringService(AuthoringCatalogueStore store, ModelPublishService publish)
{
    public async Task<IReadOnlyDictionary<string, string>> NamesForModelAsync(string modelCode, CancellationToken ct = default)
    {
        var articles = await store.LoadArticlesAsync(ct);
        return articles.Where(a => a.IsCatalogueBacked() && a.ModelCode == modelCode)
            .ToDictionary(a => a.VariantCode!, a => a.AssignedCode);
    }

    public async Task AssignAsync(string modelCode, string elementCode, string variantCode, IReadOnlyDictionary<string, string> selections, string? code, CancellationToken ct = default)
    {
        if (await publish.GetStateAsync(modelCode, ct) != TradeItemState.Draft)
        {
            throw new NamingFrozenException(modelCode);
        }
        var articles = await store.LoadArticlesAsync(ct);
        var existing = articles.FirstOrDefault(a => a.ModelCode == modelCode && a.VariantCode == variantCode);
        var trimmed = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        if (trimmed is null)
        {
            if (existing is not null) { articles.Remove(existing); await store.SaveArticlesAsync(articles, ct); }
            return;
        }
        if (existing is null)
        {
            articles.Add(new Article
            {
                Id = NextId(articles),
                AssignedCode = trimmed,
                ModelCode = modelCode,
                ElementCode = elementCode,
                VariantCode = variantCode,
                Selections = new Dictionary<string, string>(selections),
                State = TradeItemState.Draft,
            });
        }
        else { existing.AssignedCode = trimmed; }
        await store.SaveArticlesAsync(articles, ct);
    }

    public async Task AddStandaloneAsync(Article article, CancellationToken ct = default)
    {
        ValidateStandalone(article);
        var articles = await store.LoadArticlesAsync(ct);
        article.Id = NextId(articles);
        article.AssignedCode = article.AssignedCode.Trim();
        articles.Add(article);
        await store.SaveArticlesAsync(articles, ct);
    }

    public async Task UpdateStandaloneAsync(int id, Article article, CancellationToken ct = default)
    {
        ValidateStandalone(article);
        var articles = await store.LoadArticlesAsync(ct);
        var index = articles.FindIndex(a => a.Id == id && !a.IsCatalogueBacked());
        if (index < 0) { throw new InvalidOperationException($"Standalone article {id} not found."); }
        article.Id = id;
        article.AssignedCode = article.AssignedCode.Trim();
        articles[index] = article;
        await store.SaveArticlesAsync(articles, ct);
    }

    public async Task DeleteStandaloneAsync(int id, CancellationToken ct = default)
    {
        var articles = await store.LoadArticlesAsync(ct);
        if (articles.RemoveAll(a => a.Id == id && !a.IsCatalogueBacked()) > 0)
        {
            await store.SaveArticlesAsync(articles, ct);
        }
    }

    // Cascade hooks — exact predicate parity with the old VariantNaming pruning: an element's
    // variants are the bare element code or "elementCode-..." (element codes cannot contain '-').
    public async Task PruneForElementAsync(string modelCode, string elementCode, CancellationToken ct = default)
    {
        var articles = await store.LoadArticlesAsync(ct);
        var prefix = elementCode + "-";
        if (articles.RemoveAll(a => a.ModelCode == modelCode && a.VariantCode is not null
                && (a.VariantCode == elementCode || a.VariantCode.StartsWith(prefix))) > 0)
        {
            await store.SaveArticlesAsync(articles, ct);
        }
    }

    public async Task DeleteForModelAsync(string modelCode, CancellationToken ct = default)
    {
        var articles = await store.LoadArticlesAsync(ct);
        if (articles.RemoveAll(a => a.ModelCode == modelCode) > 0)
        {
            await store.SaveArticlesAsync(articles, ct);
        }
    }

    private static int NextId(List<Article> articles) => articles.Count == 0 ? 1 : articles.Max(a => a.Id) + 1;

    private static void ValidateStandalone(Article article)
    {
        if (article.ModelCode is not null || article.ElementCode is not null || article.VariantCode is not null)
        {
            throw new InvalidOperationException("Standalone articles cannot carry catalogue provenance.");
        }
        if (string.IsNullOrWhiteSpace(article.AssignedCode)) { throw new InvalidOperationException("Article code is required."); }
        if (article.ManualPrice is null) { throw new InvalidOperationException("Standalone articles need a manual price."); }
        if (article.ManualPrice < 0) { throw new InvalidOperationException("Manual price cannot be negative."); }
    }
}
