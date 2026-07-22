using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public sealed record UserSummary(string Id, string UserName, string DisplayName, IReadOnlyList<string> Roles, bool IsDeactivated);

// Real user administration, replacing SetupState's direct-write stand-in (Task 1). Still no
// UserManager/RoleManager - plain EF store + IPasswordHasher, mirroring the Identity row shape
// (normalized upper-invariant columns, fresh Security/ConcurrencyStamps) by hand. Deactivation is
// lockout-based (LockoutEnabled + LockoutEnd = MaxValue) rather than a deleted/disabled flag, so it
// composes with ASP.NET Identity's own lockout check in the sign-in path.
public sealed class UserAdminService(IDbContextFactory<FurniturePlannerContext> factory, IPasswordHasher<FurnitureUser> hasher)
{
    public async Task<List<UserSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        var roleNames = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name!, ct);
        var userRoles = await db.UserRoles.AsNoTracking().ToListAsync(ct);

        return users
            .Select(u =>
            {
                var roles = userRoles.Where(ur => ur.UserId == u.Id).Select(ur => roleNames[ur.RoleId]).OrderBy(r => r).ToList();
                var displayName = $"{u.FirstName} {u.LastName}".Trim();
                return new UserSummary(
                    u.Id,
                    u.UserName!,
                    displayName.Length == 0 ? u.UserName! : displayName,
                    roles,
                    IsDeactivated(u));
            })
            .OrderBy(u => u.UserName)
            .ToList();
    }

    public async Task CreateAsync(string userName, string firstName, string lastName, string password, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        var trimmedUserName = (userName ?? string.Empty).Trim();
        if (trimmedUserName.Length == 0) { throw new InvalidOperationException("Username is required."); }
        RequireValidPassword(password);
        ValidateRoles(roles);

        await using var db = await factory.CreateDbContextAsync(ct);
        var normalizedUserName = trimmedUserName.ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedUserName == normalizedUserName, ct))
        {
            throw new InvalidOperationException($"Username '{trimmedUserName}' is already taken.");
        }

        var user = new FurnitureUser
        {
            UserName = trimmedUserName,
            NormalizedUserName = normalizedUserName,
            FirstName = (firstName ?? string.Empty).Trim(),
            LastName = (lastName ?? string.Empty).Trim(),
            EmailConfirmed = true,
            LockoutEnabled = false,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D"),
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var roleIds = await db.Roles.Where(r => roles.Contains(r.Name)).Select(r => r.Id).ToListAsync(ct);
        db.UserRoles.AddRange(roleIds.Select(roleId => new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId }));
        await db.SaveChangesAsync(ct);
    }

    public async Task SetRolesAsync(string userId, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        ValidateRoles(roles);

        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireUserAsync(db, userId, ct);

        if (!roles.Contains(Roles.Admin))
        {
            await EnsureAdminCoverageAsync(db, userId, ct);
        }

        var existing = await db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync(ct);
        db.UserRoles.RemoveRange(existing);

        var roleIds = await db.Roles.Where(r => roles.Contains(r.Name)).Select(r => r.Id).ToListAsync(ct);
        db.UserRoles.AddRange(roleIds.Select(roleId => new IdentityUserRole<string> { UserId = userId, RoleId = roleId }));
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await RequireUserAsync(db, userId, ct);

        await EnsureAdminCoverageAsync(db, userId, ct);

        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.SecurityStamp = Guid.NewGuid().ToString("D");
        await db.SaveChangesAsync(ct);
    }

    public async Task ReactivateAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await RequireUserAsync(db, userId, ct);

        user.LockoutEnabled = false;
        user.LockoutEnd = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default)
    {
        RequireValidPassword(newPassword);

        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await RequireUserAsync(db, userId, ct);

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("D");
        await db.SaveChangesAsync(ct);
    }

    private static async Task<FurnitureUser> RequireUserAsync(FurniturePlannerContext db, string userId, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

    // Last-admin guard: refuses a change that would leave zero active admins. Only the *other*
    // active admins count as cover - a second admin that is itself deactivated does not, since
    // it can't sign in to act as admin either.
    private static async Task EnsureAdminCoverageAsync(FurniturePlannerContext db, string userId, CancellationToken ct)
    {
        var adminRoleId = await db.Roles.Where(r => r.Name == Roles.Admin).Select(r => r.Id).SingleOrDefaultAsync(ct);
        if (adminRoleId is null) { return; }

        var activeAdmins = await db.UserRoles
            .Where(ur => ur.RoleId == adminRoleId)
            .Join(db.Users, ur => ur.UserId, u => u.Id, (ur, u) => u)
            .Where(u => !(u.LockoutEnabled && u.LockoutEnd == DateTimeOffset.MaxValue))
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (activeAdmins.Contains(userId) && activeAdmins.Count == 1)
        {
            throw new InvalidOperationException("At least one active admin is required.");
        }
    }

    private static bool IsDeactivated(FurnitureUser u) => u.LockoutEnabled && u.LockoutEnd == DateTimeOffset.MaxValue;

    private static void RequireValidPassword(string? password)
    {
        if (password is null || password.Length < AuthDefaults.MinPasswordLength)
        {
            throw new InvalidOperationException($"Password must be at least {AuthDefaults.MinPasswordLength} characters.");
        }
    }

    private static void ValidateRoles(IReadOnlyList<string> roles)
    {
        if (roles is null || roles.Count == 0)
        {
            throw new InvalidOperationException("At least one role is required.");
        }
        foreach (var role in roles)
        {
            if (!Roles.All.Contains(role))
            {
                throw new InvalidOperationException($"'{role}' is not a valid role.");
            }
        }
    }
}
