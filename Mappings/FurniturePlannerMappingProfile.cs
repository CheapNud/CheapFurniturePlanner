using Mapster;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.ViewModels;

namespace CheapFurniturePlanner.Mappings;

/// <summary>
/// Mapster configuration for mapping between models and view models
/// </summary>
public static class FurniturePlannerMappingProfile
{
    public static void Configure(TypeAdapterConfig config)
    {
        ConfigureFurnitureItemMappings(config);
        ConfigureRoomPlanMappings(config);
        ConfigurePlannerFurnitureMappings(config);
    }

    private static void ConfigureFurnitureItemMappings(TypeAdapterConfig config)
    {
        // FurnitureItem to FurnitureCatalogViewModel
        config.NewConfig<FurnitureItem, FurnitureCatalogViewModel>();

        // FurnitureCatalogViewModel to FurnitureItem
        config.NewConfig<FurnitureCatalogViewModel, FurnitureItem>()
            .Map(dest => dest.Id, src => src.Id > 0 ? src.Id : 0)
            .Ignore(dest => dest.PlannerItems)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt == default ? DateTime.UtcNow : src.CreatedAt)
            .Map(dest => dest.UpdatedAt, src => DateTime.UtcNow);

        // FurnitureItem to FurniturePlannerViewModel
        config.NewConfig<FurnitureItem, FurniturePlannerViewModel>()
            .Map(dest => dest.FurnitureItemId, src => src.Id)
            .Map(dest => dest.FurnitureWidth, src => src.Width)
            .Map(dest => dest.FurnitureLength, src => src.Length)
            .Map(dest => dest.FurnitureHeight, src => src.Height)
            .Ignore(dest => dest.UIId)
            .Ignore(dest => dest.X)
            .Ignore(dest => dest.Y)
            .Ignore(dest => dest.Rotation)
            .Ignore(dest => dest.GroupID)
            .Ignore(dest => dest.CustomName)
            .Ignore(dest => dest.Notes)
            .Ignore(dest => dest.IsSelected)
            .Ignore(dest => dest.IsDragging);

        // FurnitureCatalogViewModel to FurniturePlannerViewModel
        config.NewConfig<FurnitureCatalogViewModel, FurniturePlannerViewModel>()
            .Map(dest => dest.FurnitureItemId, src => src.Id)
            .Map(dest => dest.FurnitureWidth, src => src.Width)
            .Map(dest => dest.FurnitureLength, src => src.Length)
            .Map(dest => dest.FurnitureHeight, src => src.Height)
            .Ignore(dest => dest.UIId)
            .Ignore(dest => dest.X)
            .Ignore(dest => dest.Y)
            .Ignore(dest => dest.Rotation)
            .Ignore(dest => dest.GroupID)
            .Ignore(dest => dest.CustomName)
            .Ignore(dest => dest.Notes)
            .Ignore(dest => dest.IsSelected)
            .Ignore(dest => dest.IsDragging);
    }

    private static void ConfigureRoomPlanMappings(TypeAdapterConfig config)
    {
        // RoomPlan to RoomPlanViewModel
        config.NewConfig<RoomPlan, RoomPlanViewModel>()
            .Ignore(dest => dest.FurnitureItems); // Handled separately

        // RoomPlanViewModel to RoomPlan
        config.NewConfig<RoomPlanViewModel, RoomPlan>()
            .Map(dest => dest.Id, src => src.Id > 0 ? src.Id : 0)
            .Ignore(dest => dest.FurnitureItems)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt == default ? DateTime.UtcNow : src.CreatedAt)
            .Map(dest => dest.UpdatedAt, src => DateTime.UtcNow);
    }

    private static void ConfigurePlannerFurnitureMappings(TypeAdapterConfig config)
    {
        // PlannerFurnitureItem to FurniturePlannerViewModel (for position data)
        config.NewConfig<PlannerFurnitureItem, FurniturePlannerViewModel>()
            .Map(dest => dest.UIId, src => src.UIId)
            .Map(dest => dest.X, src => src.X)
            .Map(dest => dest.Y, src => src.Y)
            .Map(dest => dest.Rotation, src => src.Rotation)
            .Map(dest => dest.GroupID, src => src.GroupId)
            .Map(dest => dest.CustomName, src => src.CustomName)
            .Map(dest => dest.Notes, src => src.Notes)
            .Map(dest => dest.FurnitureItemId, src => src.FurnitureItemId)
            // Map from related FurnitureItem
            .Map(dest => dest.Code, src => src.FurnitureItem.Code)
            .Map(dest => dest.Name, src => src.FurnitureItem.Name)
            .Map(dest => dest.Description, src => src.FurnitureItem.Description)
            .Map(dest => dest.Type, src => src.FurnitureItem.Type)
            .Map(dest => dest.FurnitureWidth, src => src.FurnitureItem.Width)
            .Map(dest => dest.FurnitureLength, src => src.FurnitureItem.Length)
            .Map(dest => dest.FurnitureHeight, src => src.FurnitureItem.Height)
            .Map(dest => dest.Weight, src => src.FurnitureItem.Weight)
            .Map(dest => dest.Color, src => src.FurnitureItem.Color)
            .Map(dest => dest.Material, src => src.FurnitureItem.Material)
            .Map(dest => dest.ImageUrl, src => src.FurnitureItem.ImageUrl)
            .Map(dest => dest.Price, src => src.FurnitureItem.Price)
            .Map(dest => dest.Brand, src => src.FurnitureItem.Brand)
            .Map(dest => dest.Model, src => src.FurnitureItem.Model)
            .Ignore(dest => dest.IsSelected)
            .Ignore(dest => dest.IsDragging);

        // FurniturePlannerViewModel to PlannerFurnitureItem (for saving)
        config.NewConfig<FurniturePlannerViewModel, PlannerFurnitureItem>()
            .Ignore(dest => dest.Id) // Let EF handle this
            .Ignore(dest => dest.RoomPlanId) // Set manually
            .Map(dest => dest.FurnitureItemId, src => src.FurnitureItemId)
            .Map(dest => dest.UIId, src => src.UIId)
            .Map(dest => dest.X, src => src.X)
            .Map(dest => dest.Y, src => src.Y)
            .Map(dest => dest.Rotation, src => src.Rotation)
            .Map(dest => dest.GroupId, src => src.GroupID)
            .Map(dest => dest.CustomName, src => src.CustomName)
            .Map(dest => dest.Notes, src => src.Notes)
            .Ignore(dest => dest.CreatedAt) // Let repository handle this
            .Ignore(dest => dest.UpdatedAt) // Let repository handle this
            .Ignore(dest => dest.RoomPlan)
            .Ignore(dest => dest.FurnitureItem);

        // PlannerOrderItemViewModel mappings (if needed for order functionality)
        config.NewConfig<FurniturePlannerViewModel, PlannerOrderItemViewModel>()
            .Ignore(dest => dest.Id)
            .Map(dest => dest.Furniture, src => src)
            .Map(dest => dest.Quantity, src => 1)
            .Map(dest => dest.UnitPrice, src => src.Price)
            .Map(dest => dest.Notes, src => src.Notes)
            .Map(dest => dest.AddedAt, src => DateTime.UtcNow);
    }
}