using CheapFurniturePlanner.Domain.Pricing;

namespace CheapFurniturePlanner.ViewModels;

/// <summary>An addable catalogue element (model + element) for the Add dialog.</summary>
public record CatalogueElementOption(
    string ModelCode, string ModelName, string ElementCode, string ElementName,
    double Width, double Depth, double Height)
{
    /// <summary>Projects every element of every model in the snapshot into a flat, addable option list.</summary>
    public static List<CatalogueElementOption> FromSnapshot(CatalogueSnapshot snapshot) =>
        snapshot.Models
            .SelectMany(model => model.Elements.Select(element =>
                new CatalogueElementOption(model.Code, model.Name, element.Code, element.Name, element.Width, element.Depth, element.Height)))
            .ToList();
}
