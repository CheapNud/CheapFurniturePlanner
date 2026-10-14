using Microsoft.AspNetCore.Authorization;

namespace CheapFurniturePlanner.Auth;

// Pages and nav blocks declare intent through these names; the role sets behind them are defined
// once, here, and registered from Program.cs. The bootstrap admin gate (UsersPage, and its nav link)
// is the single [Authorize(Roles = ...)] left on purpose: it must keep working before any policy
// configuration exists and is what lets an admin repair the rest.
public static class Policies
{
    public const string BackOffice = nameof(BackOffice);
    public const string Dock = nameof(Dock);
    public const string FirmAdministration = nameof(FirmAdministration);

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(BackOffice, policy => policy.RequireRole(Roles.Admin, Roles.Office));
        options.AddPolicy(Dock, policy => policy.RequireRole(Roles.Admin, Roles.Office, Roles.Warehouse));
        options.AddPolicy(FirmAdministration, policy => policy.RequireRole(Roles.Admin));
    }
}
