using CheapHelpers.Models.Contracts;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CheapFurniturePlanner.Models;

/// <summary>
/// Represents a room plan/layout
/// </summary>
public class RoomPlan : IEntityId
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [Column(TypeName = "decimal(8,2)")]
    public double Width { get; set; }

    [Required]
    [Column(TypeName = "decimal(8,2)")]
    public double Height { get; set; }

    [MaxLength(20)]
    public string Unit { get; set; } = "cm";

    [Required]
    public int GridSize { get; set; } = 10;

    public bool ShowGrid { get; set; } = true;
    public bool PreventOverlap { get; set; } = true;
    public bool EnableSnapping { get; set; } = true;

    [MaxLength(50)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<PlannerFurnitureItem> FurnitureItems { get; set; } = new List<PlannerFurnitureItem>();
}
