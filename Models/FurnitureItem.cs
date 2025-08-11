using CheapHelpers.Models.Contracts;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CheapFurniturePlanner.Models;

/// <summary>
/// Represents a furniture item in the catalog
/// </summary>
public class FurnitureItem : IEntityId, IEntityCode
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    public FurnitureType Type { get; set; }

    [Required]
    [Column(TypeName = "decimal(8,2)")]
    public double Width { get; set; }

    [Required]
    [Column(TypeName = "decimal(8,2)")]
    public double Length { get; set; }

    [Required]
    [Column(TypeName = "decimal(8,2)")]
    public double Height { get; set; }

    [Column(TypeName = "decimal(8,2)")]
    public double? Weight { get; set; }

    [MaxLength(100)]
    public string? Color { get; set; }

    [MaxLength(100)]
    public string? Material { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Price { get; set; }

    [MaxLength(50)]
    public string? Brand { get; set; }

    [MaxLength(100)]
    public string? Model { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<PlannerFurnitureItem> PlannerItems { get; set; } = new List<PlannerFurnitureItem>();
}
