namespace CheapFurniturePlanner.Domain.Catalog;

// A first-class catalogue citizen — the "flat article" half of the article<->element bridge.
// Two flavours in one entity:
//  - catalogue-backed: ModelCode/ElementCode/VariantCode set together (all-or-nothing); prices via
//    the engine through its provenance; created by naming a variant in the Studio.
//  - standalone (legacy/dropship): provenance null; carries its own ManualPrice and optional
//    SupplierRef (free text — no Supplier entity yet); no production info by definition.
// AssignedCode is curated human vocabulary (K7E-style) with NO global uniqueness — order lines
// reference Id + provenance, never the raw code. Selections stores the BOM-significant choices
// (incl. the __MATERIAL__ entry) explicitly, for the reverse direction (article -> configurator)
// and a future migration from variant-code string equality to value-set matching.
public class Article
{
    public int Id { get; set; }
    public required string AssignedCode { get; set; }
    public string? Name { get; set; }
    public string? ModelCode { get; set; }
    public string? ElementCode { get; set; }
    public string? VariantCode { get; set; }
    public Dictionary<string, string> Selections { get; set; } = [];
    public TradeItemState State { get; set; } = TradeItemState.Draft;
    public decimal? ManualPrice { get; set; }
    public string? SupplierRef { get; set; }

    public bool IsCatalogueBacked() => ElementCode is not null;
}
