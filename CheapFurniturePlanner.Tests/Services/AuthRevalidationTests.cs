using System.Security.Claims;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors UserAdminServiceTests. These tests call
// HttpContextAuthenticationStateProvider.ValidateAsync directly - the piece the circuit's
// PeriodicTimer loop calls on every tick - so there's no timer wait involved.
public class AuthRevalidationTests
{
    private const string SecurityStampClaimType = "AspNet.Identity.SecurityStamp";

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static async Task<(IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection)> NewFactoryAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        await using (var migrateContext = new FurniturePlannerContext(options))
        {
            await migrateContext.Database.MigrateAsync();
            await RoleSeeder.SeedAsync(migrateContext);
        }
        return (new TestDbContextFactory(options), connection);
    }

    // Mirrors what the default IUserClaimsPrincipalFactory<FurnitureUser> actually puts on the
    // Identity cookie (verified directly against it): NameIdentifier, role claims, and an
    // AspNet.Identity.SecurityStamp claim - always present, since the EF store supports it.
    private static ClaimsPrincipal BuildPrincipal(string userId, IEnumerable<string> roles, string? securityStamp)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (securityStamp is not null)
        {
            claims.Add(new Claim(SecurityStampClaimType, securityStamp));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public async Task Validate_ActiveUserWithMatchingRoles_True()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("clerk", "C", "L", "secret1", [Roles.Office]);
        var user = (await service.ListAsync()).Single();

        await using var db = await factory.CreateDbContextAsync();
        var stamp = await db.Users.Where(u => u.Id == user.Id).Select(u => u.SecurityStamp).SingleAsync();
        var principal = BuildPrincipal(user.Id, [Roles.Office], stamp);

        Assert.True(await HttpContextAuthenticationStateProvider.ValidateAsync(principal, db));
    }

    [Fact]
    public async Task Validate_DeactivatedUser_False()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("clerk", "C", "L", "secret1", [Roles.Office]);
        var user = (await service.ListAsync()).Single();

        await using var seedDb = await factory.CreateDbContextAsync();
        var stamp = await seedDb.Users.Where(u => u.Id == user.Id).Select(u => u.SecurityStamp).SingleAsync();
        var principal = BuildPrincipal(user.Id, [Roles.Office], stamp);

        await service.DeactivateAsync(user.Id);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await HttpContextAuthenticationStateProvider.ValidateAsync(principal, db));
    }

    [Fact]
    public async Task Validate_RoleChanged_False()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("admin1", "A", "One", "secret1", [Roles.Admin]);
        await service.CreateAsync("admin2", "A", "Two", "secret1", [Roles.Admin]);
        var target = (await service.ListAsync()).Single(u => u.UserName == "admin1");

        await using var seedDb = await factory.CreateDbContextAsync();
        var stamp = await seedDb.Users.Where(u => u.Id == target.Id).Select(u => u.SecurityStamp).SingleAsync();
        // Principal still carries the old Admin role claim - SetRolesAsync doesn't rotate the
        // security stamp, so the stamp check alone wouldn't catch this; role-set equality does.
        var principal = BuildPrincipal(target.Id, [Roles.Admin], stamp);

        await service.SetRolesAsync(target.Id, [Roles.Office]);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await HttpContextAuthenticationStateProvider.ValidateAsync(principal, db));
    }

    [Fact]
    public async Task Validate_DeletedUser_False()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var principal = BuildPrincipal(Guid.NewGuid().ToString(), [Roles.Office], Guid.NewGuid().ToString());

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await HttpContextAuthenticationStateProvider.ValidateAsync(principal, db));
    }

    [Fact]
    public async Task Validate_PasswordReset_InvalidatesWhenStampClaimPresent()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("clerk", "C", "L", "secret1", [Roles.Office]);
        var user = (await service.ListAsync()).Single();

        await using var seedDb = await factory.CreateDbContextAsync();
        var preResetStamp = await seedDb.Users.Where(u => u.Id == user.Id).Select(u => u.SecurityStamp).SingleAsync();
        var principal = BuildPrincipal(user.Id, [Roles.Office], preResetStamp);

        await service.ResetPasswordAsync(user.Id, "newsecret1");

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await HttpContextAuthenticationStateProvider.ValidateAsync(principal, db));
    }
}
