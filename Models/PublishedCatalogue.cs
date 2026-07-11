using CheapHelpers.Models.Contracts;
using System.ComponentModel.DataAnnotations;

namespace CheapFurniturePlanner.Models;

/// <summary>A published, immutable catalogue snapshot. Current = the version with the greatest EffectiveDate &lt;= now (IsCurrent marks the most recently published, informational).</summary>
public class PublishedCatalogue : IEntityId
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string Version { get; set; } = "";

    [Required]
    [MaxLength(64)]
    public string ContentHash { get; set; } = "";

    [Required]
    public string BundleJson { get; set; } = "";

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this version becomes the served ("current") catalogue. Current = the row with the
    /// greatest EffectiveDate that is &lt;= now.</summary>
    public DateTime EffectiveDate { get; set; }

    public bool IsCurrent { get; set; }
}
