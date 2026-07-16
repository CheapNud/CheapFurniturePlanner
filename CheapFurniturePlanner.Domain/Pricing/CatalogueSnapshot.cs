using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Serialization;

namespace CheapFurniturePlanner.Domain.Pricing;

public class CatalogueSnapshot
{
    public required string Version { get; set; }
    public string ContentHash { get; set; } = "";
    public List<FurnitureModel> Models { get; set; } = [];

    // Articles: the flat orderable identities (catalogue-backed variants + standalone/dropship).
    // NOT a master list — stored in its own authoring document (AuthoringArticlesDocument), zeroed
    // out of the masters doc by SaveMastersAsync exactly like Models. The pricing engine ignores it.
    public List<Article> Articles { get; set; } = [];

    // Master lists (PriceGroups..Markets below): shared, non-model-specific reference data. Any new
    // master list added here must also be copied in AuthoringCatalogueStore.SeedFromAsync's masters
    // clone, or it will silently be dropped from the authoring store on seed/republish.
    public List<PriceGroup> PriceGroups { get; set; } = [];
    public List<FabricGroup> FabricGroups { get; set; } = [];
    public List<Operation> Operations { get; set; } = [];
    public List<FrameBody> FrameBodies { get; set; } = [];
    public List<Material> Materials { get; set; } = [];
    public List<SprayPrice> SprayPrices { get; set; } = [];
    public List<FixedSurcharge> FixedSurcharges { get; set; } = [];
    public List<ChoiceSurcharge> ChoiceSurcharges { get; set; } = [];
    public List<CombinationPriceRule> CombinationPriceRules { get; set; } = [];
    public List<MarketParameters> Markets { get; set; } = [];

    // Hash covers everything except ContentHash itself: save/mutate/restore around
    // serialization since CatalogueSnapshot is mutable and STJ has no per-call property exclusion here.
    public string ComputeContentHash()
    {
        var savedContentHash = ContentHash;
        ContentHash = "";
        try
        {
            return CanonicalJson.Sha256Hex(this);
        }
        finally
        {
            ContentHash = savedContentHash;
        }
    }
}
