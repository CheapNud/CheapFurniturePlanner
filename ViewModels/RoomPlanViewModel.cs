using System.ComponentModel.DataAnnotations;

namespace CheapFurniturePlanner.ViewModels;

/// <summary>
/// ViewModel for room plans
/// </summary>
public class RoomPlanViewModel
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [Range(50, 2000)]
    public double Width { get; set; } = 500;

    [Required]
    [Range(50, 2000)]
    public double Height { get; set; } = 400;

    [MaxLength(20)]
    public string Unit { get; set; } = "cm";

    [Range(5, 50)]
    public int GridSize { get; set; } = 10;

    public bool ShowGrid { get; set; } = true;
    public bool PreventOverlap { get; set; } = true;
    public bool EnableSnapping { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<FurniturePlannerViewModel> FurnitureItems { get; set; } = new();
}
