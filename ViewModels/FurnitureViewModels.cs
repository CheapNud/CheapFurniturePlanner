using CheapFurniturePlanner.Models;
using System.Text.Json.Serialization;

namespace CheapFurniturePlanner.ViewModels;

/// <summary>
/// ViewModel for furniture items in the planner (equivalent to ArticleViewModel)
/// </summary>
public class FurniturePlannerViewModel
{
    /// <summary>
    /// Unique UI identifier for this instance in the planner
    /// </summary>
    public int UIId { get; set; }

    /// <summary>
    /// Reference to the furniture catalog item
    /// </summary>
    public int FurnitureItemId { get; set; }

    /// <summary>
    /// Furniture item code
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Furniture type
    /// </summary>
    public FurnitureType Type { get; set; }

    /// <summary>
    /// Position X coordinate in the room plan
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Position Y coordinate in the room plan
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Rotation angle in degrees
    /// </summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Width of the furniture item (in cm or specified unit)
    /// </summary>
    public double? FurnitureWidth { get; set; }

    /// <summary>
    /// Length of the furniture item (in cm or specified unit)
    /// </summary>
    public double? FurnitureLength { get; set; }

    /// <summary>
    /// Height of the furniture item (in cm or specified unit)
    /// </summary>
    public double? FurnitureHeight { get; set; }

    /// <summary>
    /// Weight of the furniture item
    /// </summary>
    public double? Weight { get; set; }

    /// <summary>
    /// Group ID for grouping multiple items together
    /// </summary>
    public int? GroupID { get; set; }

    /// <summary>
    /// Custom name for this specific instance
    /// </summary>
    public string? CustomName { get; set; }

    /// <summary>
    /// Notes for this placement
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Color of the furniture
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Material of the furniture
    /// </summary>
    public string? Material { get; set; }

    /// <summary>
    /// Image URL for display
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Price of the furniture item
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Brand name
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>
    /// Model name
    /// </summary>
    public string? Model { get; set; }

    // UI-specific properties
    [JsonIgnore]
    public bool IsSelected { get; set; }

    [JsonIgnore]
    public bool IsDragging { get; set; }

    /// <summary>
    /// Creates a clone of this furniture item for adding to the planner
    /// </summary>
    public FurniturePlannerViewModel PlannerClone()
    {
        return new FurniturePlannerViewModel
        {
            UIId = 0, // Will be set when added to planner
            FurnitureItemId = FurnitureItemId,
            Code = Code,
            Name = Name,
            Description = Description,
            Type = Type,
            X = 0, // Will be positioned when added
            Y = 0,
            Rotation = 0,
            FurnitureWidth = FurnitureWidth,
            FurnitureLength = FurnitureLength,
            FurnitureHeight = FurnitureHeight,
            Weight = Weight,
            Color = Color,
            Material = Material,
            ImageUrl = ImageUrl,
            Price = Price,
            Brand = Brand,
            Model = Model
        };
    }
}
