using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors DiscountServiceTests: in-memory SQLite, migrated schema.
public class IdentityWiringTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (new TestDbContextFactory(options), connection);
    }

    [Fact]
    public async Task RolesSeeded_Idempotently()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;

        await using (var db = await factory.CreateDbContextAsync())
        {
            await RoleSeeder.SeedAsync(db);
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            await RoleSeeder.SeedAsync(db);
        }

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal(3, await verify.Roles.CountAsync());
        Assert.Equal(Roles.All.OrderBy(r => r), (await verify.Roles.Select(r => r.Name!).ToListAsync()).OrderBy(r => r));
    }

    [Fact]
    public async Task FirstAdmin_CanVerifyPassword()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;

        await using (var db = await factory.CreateDbContextAsync())
        {
            await RoleSeeder.SeedAsync(db);
        }

        var hasher = new PasswordHasher<FurnitureUser>();
        var userAdmin = new UserAdminService(factory, hasher);
        var setupState = new SetupState(factory, userAdmin);

        var created = await setupState.CreateFirstAdminAsync("admin", "Ada", "Min", "secret1");

        await using var verify = await factory.CreateDbContextAsync();
        var stored = await verify.Users.SingleAsync(u => u.Id == created.Id);

        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(stored, stored.PasswordHash!, "secret1"));

        var isAdmin = await verify.UserRoles
            .Join(verify.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(x => x.UserId == created.Id && x.Name == Roles.Admin);
        Assert.True(isAdmin);
    }

    // A second stale circuit's SetupState instance can have its _anyUsers cache primed false
    // before setup completes elsewhere - CreateFirstAdminAsync must re-check the store itself,
    // not trust the cache, or it mints a second unauthenticated Admin.
    [Fact]
    public async Task CreateFirstAdmin_AfterSetupCompleted_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;

        await using (var db = await factory.CreateDbContextAsync())
        {
            await RoleSeeder.SeedAsync(db);
        }

        var hasher = new PasswordHasher<FurnitureUser>();
        var userAdmin = new UserAdminService(factory, hasher);
        var staleSetupState = new SetupState(factory, userAdmin);
        Assert.False(await staleSetupState.AnyUsersAsync()); // primes the cache empty

        var firstSetupState = new SetupState(factory, userAdmin);
        await firstSetupState.CreateFirstAdminAsync("admin", "Ada", "Min", "secret1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => staleSetupState.CreateFirstAdminAsync("intruder", "In", "Truder", "secret2"));

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal(1, await verify.Users.CountAsync());
    }
}
