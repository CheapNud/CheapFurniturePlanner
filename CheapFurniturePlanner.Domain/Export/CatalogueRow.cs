namespace CheapFurniturePlanner.Domain.Export;

// One orderable price point of a published catalogue: the flat projection consumed by the
// export writers. A projection, not a domain layer - produced only by CatalogueFlattener.
public sealed record CatalogueRow(
    string CatalogueVersion,
    string ContentHash,
    string ModelCode,
    string ModelName,
    string? CollectionCode,
    string ElementCode,
    string ElementName,
    string PriceGroupCode,
    string MarketCode,
    decimal Price);
