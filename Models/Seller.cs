namespace CheapFurniturePlanner.Models;

public class Seller
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Multiplier { get; set; } = 1m;
}
