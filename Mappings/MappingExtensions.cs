using MapsterMapper;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.ViewModels;

namespace CheapFurniturePlanner.Mappings;

/// <summary>
/// Extension methods for additional mapping functionality
/// </summary>
public static class MappingExtensions
{
    /// <summary>
    /// Maps a FurnitureItem to FurniturePlannerViewModel with position data from PlannerFurnitureItem
    /// </summary>
    public static FurniturePlannerViewModel ToFurniturePlannerViewModel(
        this FurnitureItem furnitureItem,
        PlannerFurnitureItem? plannerItem = null,
        IMapper? mapper = null)
    {
        var viewModel = mapper?.Map<FurniturePlannerViewModel>(furnitureItem) ??
                       new FurniturePlannerViewModel
                       {
                           FurnitureItemId = furnitureItem.Id,
                           Code = furnitureItem.Code,
                           Name = furnitureItem.Name,
                           Description = furnitureItem.Description,
                           Type = furnitureItem.Type,
                           FurnitureWidth = furnitureItem.Width,
                           FurnitureLength = furnitureItem.Length,
                           FurnitureHeight = furnitureItem.Height,
                           Weight = furnitureItem.Weight,
                           Color = furnitureItem.Color,
                           Material = furnitureItem.Material,
                           ImageUrl = furnitureItem.ImageUrl,
                           Price = furnitureItem.Price,
                           Brand = furnitureItem.Brand,
                           Model = furnitureItem.Model
                       };

        if (plannerItem != null)
        {
            viewModel.UIId = plannerItem.UIId;
            viewModel.X = plannerItem.X;
            viewModel.Y = plannerItem.Y;
            viewModel.Rotation = plannerItem.Rotation;
            viewModel.GroupID = plannerItem.GroupId;
            viewModel.CustomName = plannerItem.CustomName;
            viewModel.Notes = plannerItem.Notes;
        }

        return viewModel;
    }

    /// <summary>
    /// Creates a deep clone of a FurniturePlannerViewModel for duplication
    /// </summary>
    public static FurniturePlannerViewModel DeepClone(this FurniturePlannerViewModel source)
    {
        return new FurniturePlannerViewModel
        {
            UIId = 0, // Will be set when added
            FurnitureItemId = source.FurnitureItemId,
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            Type = source.Type,
            X = source.X,
            Y = source.Y,
            Rotation = source.Rotation,
            FurnitureWidth = source.FurnitureWidth,
            FurnitureLength = source.FurnitureLength,
            FurnitureHeight = source.FurnitureHeight,
            Weight = source.Weight,
            GroupID = null, // Don't copy group assignment
            CustomName = source.CustomName,
            Notes = source.Notes,
            Color = source.Color,
            Material = source.Material,
            ImageUrl = source.ImageUrl,
            Price = source.Price,
            Brand = source.Brand,
            Model = source.Model,
            IsSelected = false,
            IsDragging = false
        };
    }

    /// <summary>
    /// Creates an order item from a furniture planner item
    /// </summary>
    public static PlannerOrderItemViewModel ToOrderItem(this FurniturePlannerViewModel source, int quantity = 1)
    {
        return new PlannerOrderItemViewModel
        {
            Furniture = source,
            Quantity = quantity,
            UnitPrice = source.Price,
            Notes = source.Notes,
            AddedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Applies position data from one furniture item to another
    /// </summary>
    public static void ApplyPositionData(this FurniturePlannerViewModel target, FurniturePlannerViewModel source)
    {
        target.X = source.X;
        target.Y = source.Y;
        target.Rotation = source.Rotation;
        target.GroupID = source.GroupID;
        target.CustomName = source.CustomName;
        target.Notes = source.Notes;
    }
}