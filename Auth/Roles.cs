namespace CheapFurniturePlanner.Auth;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Office = "Office";
    public const string Mechanic = "Mechanic";
    public const string Warehouse = "Warehouse";
    public static readonly string[] All = [Admin, Office, Mechanic, Warehouse];
}
