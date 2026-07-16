using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Domain.Production;

// The forward half of the article bridge: a configuration (element + composed variant code)
// resolves to its catalogue-backed Article by variant-code string equality. Null means un-named —
// callers fall back to the composed code (same convention as ProductionIdentityResolver). The
// reverse direction needs no method: a catalogue-backed article's Selections dictionary is the
// payload a configurator is populated from; standalone articles skip the configurator entirely.
public static class ArticleResolver
{
    public static Article? ResolveByConfiguration(CatalogueSnapshot snapshot, string elementCode, string variantCode) =>
        snapshot.Articles.FirstOrDefault(a => a.IsCatalogueBacked() && a.ElementCode == elementCode && a.VariantCode == variantCode);
}
