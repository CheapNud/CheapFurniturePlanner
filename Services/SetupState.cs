using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// First-run gate + first-admin creation. AnyUsersAsync is checked by the router on every
// navigation (see Routes.razor) and by SetupPage itself; the result is cached on the instance so
// repeat navigations within the same circuit don't re-query the DB. Scoped DI lifetime means one
// instance lives for a whole Blazor Server circuit's session (this is a single-user desktop app,
// so there is realistically only ever one circuit at a time) - no static needed to survive
// across navigations, and per-instance caching keeps tests (each with their own DB) isolated.
//
// CreateFirstAdminAsync/CreateDemoAdminAsync write directly to the Identity store + hash the
// password with IPasswordHasher - no UserManager/SignInManager, per the interactive-circuit
// restriction. This is a minimal stand-in for user creation; Task 2's UserAdminService.CreateAsync
// replaces it once user administration exists, at which point SetupPage should call that instead.
public sealed class SetupState(IDbContextFactory<FurniturePlannerContext> factory, IPasswordHasher<FurnitureUser> passwordHasher)
{
    private bool? _anyUsers;

    public async Task<bool> AnyUsersAsync(CancellationToken ct = default)
    {
        if (_anyUsers is bool cached)
        {
            return cached;
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        _anyUsers = await db.Users.AnyAsync(ct);
        return _anyUsers.Value;
    }

    public async Task<FurnitureUser> CreateFirstAdminAsync(string userName, string firstName, string lastName, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await using var db = await factory.CreateDbContextAsync(ct);

        var user = new FurnitureUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = true,
            LockoutEnabled = false, // deactivation is a deliberate admin action (Task 2), not failed-attempt lockout
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D"),
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var adminRole = await db.Roles.SingleAsync(r => r.Name == Roles.Admin, ct);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = adminRole.Id });
        await db.SaveChangesAsync(ct);

        _anyUsers = true;
        return user;
    }

    // Throwaway demo admin - visible password returned so the caller (SetupPage) can display it
    // once. Not persisted anywhere else; no seeded credentials ship in the repo.
    public Task<(FurnitureUser User, string Password)> CreateDemoAdminAsync(CancellationToken ct = default) =>
        CreateDemoAdminCoreAsync(ct);

    private async Task<(FurnitureUser, string)> CreateDemoAdminCoreAsync(CancellationToken ct)
    {
        var password = $"demo-{Guid.NewGuid():N}"[..16];
        var user = await CreateFirstAdminAsync("demo", "Demo", "Admin", password, ct);
        return (user, password);
    }

}
