using CheapFurniturePlanner.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Auth;

// Idempotent seeding for the fixed Admin/Office/Mechanic role set. Runs once per app startup
// (see Program.cs) and is exercised directly by IdentityWiringTests. Plain EF rather than
// RoleManager - these are three fixed system roles, not user-managed data, so the extra
// machinery (normalization, concurrency stamps) buys nothing here.
public static class RoleSeeder
{
    public static async Task SeedAsync(FurniturePlannerContext context, CancellationToken ct = default)
    {
        var existing = await context.Roles.Select(r => r.Name).ToListAsync(ct);
        foreach (var roleName in Roles.All)
        {
            if (!existing.Contains(roleName))
            {
                context.Roles.Add(new IdentityRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
            }
        }
        await context.SaveChangesAsync(ct);
    }
}
