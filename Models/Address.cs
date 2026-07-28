namespace CheapFurniturePlanner.Models;

// The one address table every party references. Mutable by design - a corrected typo should
// reach every open order; contractual data (prices) snapshots, location data does not.
public class Address
{
    public int Id { get; set; }
    public required string Street { get; set; }
    public required string Number { get; set; }
    public string? Box { get; set; }
    public required string PostalCode { get; set; }
    public required string City { get; set; }
    public string CountryCode { get; set; } = "BE";
    public int? RegionId { get; set; }
    public Region? Region { get; set; }

    public string ToOneLine() =>
        $"{Street} {Number}{(Box is null ? "" : $" box {Box}")}, {PostalCode} {City}";
}
