using CheapHelpers.Models.Contracts;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CheapFurniturePlanner.Models;

/// <summary>
/// Represents a furniture item placed in a room plan
/// </summary>
public class PlannerFurnitureItem : IEntityId
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int RoomPlanId { get; set; }

    // Legacy flat-catalog link — no longer the planner's source. New placements leave this null.
    public int? FurnitureItemId { get; set; }

    /// <summary>
    /// Unique identifier for this instance in the planner (for UI purposes)
    /// </summary>
    [Required]
    public int UIId { get; set; }

    [Required]
    [Column(TypeName = "decimal(8,2)")]
    public double X { get; set; }

    [Required]
    [Column(TypeName = "decimal(8,2)")]
    public double Y { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public double Rotation { get; set; } = 0;

    /// <summary>
    /// Optional group ID for grouping multiple furniture items together
    /// </summary>
    public int? GroupId { get; set; }

    /// <summary>
    /// Custom name for this specific instance (optional)
    /// </summary>
    [MaxLength(200)]
    public string? CustomName { get; set; }

    /// <summary>
    /// Custom notes for this placement
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // --- configuration payload (Phase 2) ---
    /// <summary>Element code from the catalogue snapshot this placement was configured against.</summary>
    [MaxLength(64)]
    public string? ElementCode { get; set; }

    /// <summary>Catalogue snapshot version the configuration was resolved against.</summary>
    [MaxLength(64)]
    public string? CatalogueVersion { get; set; }

    /// <summary>JSON map of option-definition code -> chosen option-choice code.</summary>
    public string? SelectionsJson { get; set; }

    [MaxLength(64)]
    public string? FabricColorCode { get; set; }

    [MaxLength(128)]
    public string? CachedVariantCode { get; set; }

    [Column(TypeName = "REAL")]
    public decimal? CachedUnitPrice { get; set; }

    // Navigation properties
    [ForeignKey(nameof(RoomPlanId))]
    public virtual RoomPlan RoomPlan { get; set; } = null!;

    [ForeignKey(nameof(FurnitureItemId))]
    public virtual FurnitureItem? FurnitureItem { get; set; }
}
