using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.Catalogue;

public interface ICatalogueSource
{
    Task<CatalogueSnapshot> GetCurrentAsync();
    void Invalidate();
}
