namespace CheapFurniturePlanner.Models;

// Flat routing tag for delivery planning - legacy proved regions never needed hierarchy, only
// governance (auto-vivified duplicate codes were a recurring cleanup chore there; here the
// lookup is CRUD-managed and referenced by FK).
public class Region
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
}
