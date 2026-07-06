using CheapHelpers.Models.Contracts;
using System.ComponentModel.DataAnnotations;

namespace CheapFurniturePlanner.Models;

/// <summary>A published, immutable catalogue snapshot. Exactly one row has IsCurrent = true.</summary>
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

    public bool IsCurrent { get; set; }
}
