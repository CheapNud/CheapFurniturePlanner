using CheapFurniturePlanner.Models;
using System.ComponentModel.DataAnnotations;

namespace CheapFurniturePlanner.ViewModels;

/// <summary>
/// ViewModel for furniture catalog items
/// </summary>
public class FurnitureCatalogViewModel
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public FurnitureType Type { get; set; }

    [Required]
    [Range(1, 1000)]
    public double Width { get; set; }

    [Required]
    [Range(1, 1000)]
    public double Length { get; set; }

    [Required]
    [Range(1, 300)]
    public double Height { get; set; }

    [Range(0.1, 500)]
    public double? Weight { get; set; }

    [MaxLength(100)]
    public string? Color { get; set; }

    [MaxLength(100)]
    public string? Material { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [Range(0, 999999.99)]
    public decimal? Price { get; set; }

    [MaxLength(50)]
    public string? Brand { get; set; }

    [MaxLength(100)]
    public string? Model { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Creates a planner view model from this catalog item
    /// </summary>
    public FurniturePlannerViewModel ToPlannerViewModel()
    {
        return new FurniturePlannerViewModel
        {
            FurnitureItemId = Id,
            Code = Code,
            Name = Name,
            Description = Description,
            Type = Type,
            FurnitureWidth = Width,
            FurnitureLength = Length,
            FurnitureHeight = Height,
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
