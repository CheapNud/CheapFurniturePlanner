using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.ViewModels;

namespace CheapFurniturePlanner.Configurator;

/// <summary>
/// Repopulates the display fields (Code/Name/dimensions) of an element placement loaded from the
/// database. <see cref="Models.PlannerFurnitureItem"/> persists no name/dimensions for element
/// placements (only ElementCode + configuration), so the Mapster map leaves Code/Name empty and the
/// Furniture* dimensions null - those must be resolved against the current catalogue snapshot on load,
/// otherwise the planner canvas renders the placement as an invisible 0x0 box.
/// </summary>
public static class PlannerElementHydrator
{
    /// <summary>
    /// Looks up <paramref name="placement"/>'s ElementCode in <paramref name="snapshot"/> and, if found,
    /// fills in Code/Name/FurnitureWidth/FurnitureLength/FurnitureHeight. No-ops for placements without
    /// an ElementCode (legacy flat-catalog placements) or for a dangling code that no longer exists in
    /// the snapshot - the config panel already surfaces the latter as "unavailable".
    /// </summary>
    public static void Hydrate(FurniturePlannerViewModel placement, CatalogueSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(placement.ElementCode))
        {
            return;
        }

        var element = snapshot.Models.SelectMany(m => m.Elements).FirstOrDefault(e => e.Code == placement.ElementCode);
        if (element is null)
        {
            return;
        }

        placement.Code = placement.ElementCode;
        placement.Name = element.Name;
        placement.FurnitureWidth = element.Width;
        placement.FurnitureLength = element.Depth;
        placement.FurnitureHeight = element.Height;
    }
}
