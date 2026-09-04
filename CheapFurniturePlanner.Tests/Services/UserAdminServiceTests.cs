using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors IdentityWiringTests: in-memory SQLite, migrated schema, roles seeded.
public class UserAdminServiceTests
{
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

    [Fact]
    public async Task CreateAndList_RoundTrips_RolesAndDisplayName()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());

        await service.CreateAsync("jdoe", "John", "Doe", "secret1", [Roles.Office]);

        var user = Assert.Single(await service.ListAsync());
        Assert.Equal("jdoe", user.UserName);
        Assert.Equal("John Doe", user.DisplayName);
        Assert.Equal([Roles.Office], user.Roles);
        Assert.False(user.IsDeactivated);
    }

    [Fact]
    public async Task Create_NoNames_DisplayNameFallsBackToUserName()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());

        await service.CreateAsync("noname", "", "", "secret1", [Roles.Office]);

        var user = Assert.Single(await service.ListAsync());
        Assert.Equal("noname", user.DisplayName);
    }

    [Fact]
    public async Task Create_DuplicateUserName_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("dupe", "A", "B", "secret1", [Roles.Office]);

        // Case-insensitive: normalized-column uniqueness, not raw-string uniqueness.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("DUPE", "C", "D", "secret1", [Roles.Office]));
    }

    [Fact]
    public async Task Create_InvalidRole_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("bob", "Bob", "X", "secret1", ["NotARole"]));
    }

    [Fact]
    public async Task Create_NoRoles_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("bob", "Bob", "X", "secret1", []));
    }

    [Fact]
    public async Task Create_ShortPassword_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("bob", "Bob", "X", "abc", [Roles.Office]));
    }

    // Sign-in compatibility + cross-check: a hash produced by the service must verify with a
    // plain IPasswordHasher, and the stored row shape (normalized column, stamp lengths) must
    // match a user created through Task 1's Identity-store path (SetupState, now itself backed
    // by this service) - proving there's one consistent row shape, not two divergent ones.
    [Fact]
    public async Task Create_HashVerifies_AndRowShapeMatchesTask1Path()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var hasher = new PasswordHasher<FurnitureUser>();
        var service = new UserAdminService(factory, hasher);
        var setupState = new SetupState(factory, service);

        // CreateFirstAdminAsync must run while the store is still empty - it now refuses once any
        // user exists (Task 3's guard), so the setup path runs first, direct creation second.
        var task1User = await setupState.CreateFirstAdminAsync("task1user", "Task", "One", "secret2");
        await service.CreateAsync("directuser", "Di", "Rect", "secret1", [Roles.Office]);

        await using var db = await factory.CreateDbContextAsync();
        var direct = await db.Users.SingleAsync(u => u.UserName == "directuser");
        var task1 = await db.Users.SingleAsync(u => u.Id == task1User.Id);

        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(direct, direct.PasswordHash!, "secret1"));
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(task1, task1.PasswordHash!, "secret2"));

        Assert.Equal(direct.UserName!.ToUpperInvariant(), direct.NormalizedUserName);
        Assert.Equal(task1.UserName!.ToUpperInvariant(), task1.NormalizedUserName);
        Assert.Equal(36, direct.SecurityStamp!.Length);
        Assert.Equal(36, task1.SecurityStamp!.Length);
        Assert.Equal(36, direct.ConcurrencyStamp!.Length);
        Assert.Equal(36, task1.ConcurrencyStamp!.Length);
        Assert.NotEqual(direct.SecurityStamp, direct.ConcurrencyStamp);
    }

    [Fact]
    public async Task SetRoles_RoundTrips()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("multi", "M", "U", "secret1", [Roles.Office]);
        var userId = (await service.ListAsync()).Single().Id;

        await service.SetRolesAsync(userId, [Roles.Office, Roles.Mechanic]);

        var roles = (await service.ListAsync()).Single().Roles;
        Assert.Equal(new[] { Roles.Mechanic, Roles.Office }, roles.OrderBy(r => r));
    }

    [Fact]
    public async Task SetRoles_InvalidRole_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("multi", "M", "U", "secret1", [Roles.Office]);
        var userId = (await service.ListAsync()).Single().Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetRolesAsync(userId, ["Nope"]));
    }

    [Fact]
    public async Task LastAdminGuard_SetRoles_RemovingOnlyActiveAdmin_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("admin1", "A", "One", "secret1", [Roles.Admin]);
        var adminId = (await service.ListAsync()).Single().Id;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetRolesAsync(adminId, [Roles.Office]));
        Assert.Equal("At least one active admin is required.", ex.Message);
    }

    [Fact]
    public async Task LastAdminGuard_Deactivate_OnlyActiveAdmin_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("admin1", "A", "One", "secret1", [Roles.Admin]);
        var adminId = (await service.ListAsync()).Single().Id;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeactivateAsync(adminId));
        Assert.Equal("At least one active admin is required.", ex.Message);
    }

    [Fact]
    public async Task LastAdminGuard_DeactivatedSecondAdmin_DoesNotCountAsCover()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("admin1", "A", "One", "secret1", [Roles.Admin]);
        await service.CreateAsync("admin2", "A", "Two", "secret1", [Roles.Admin]);
        var users = await service.ListAsync();
        var admin1Id = users.Single(u => u.UserName == "admin1").Id;
        var admin2Id = users.Single(u => u.UserName == "admin2").Id;

        // Deactivate the would-be "cover" admin first - it no longer counts as cover for admin1.
        await service.DeactivateAsync(admin2Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeactivateAsync(admin1Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetRolesAsync(admin1Id, [Roles.Office]));
    }

    [Fact]
    public async Task Deactivate_WithCoverAdmin_Succeeds_AndReactivate_RoundTrips()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("admin1", "A", "One", "secret1", [Roles.Admin]);
        await service.CreateAsync("admin2", "A", "Two", "secret1", [Roles.Admin]);
        var targetId = (await service.ListAsync()).Single(u => u.UserName == "admin2").Id;

        await service.DeactivateAsync(targetId);
        Assert.True((await service.ListAsync()).Single(u => u.Id == targetId).IsDeactivated);

        await service.ReactivateAsync(targetId);
        Assert.False((await service.ListAsync()).Single(u => u.Id == targetId).IsDeactivated);
    }

    // Deactivation must rotate the security stamp - otherwise SecurityStampValidator's periodic
    // revalidation (AuthRevalidationTests) never fires for a principal minted before deactivation,
    // since the stamp claim it compares against still matches.
    [Fact]
    public async Task Deactivate_RotatesSecurityStamp()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await service.CreateAsync("admin1", "A", "One", "secret1", [Roles.Admin]);
        await service.CreateAsync("target", "T", "Get", "secret1", [Roles.Office]);
        var targetId = (await service.ListAsync()).Single(u => u.UserName == "target").Id;

        await using var before = await factory.CreateDbContextAsync();
        var stampBefore = await before.Users.Where(u => u.Id == targetId).Select(u => u.SecurityStamp).SingleAsync();

        await service.DeactivateAsync(targetId);

        await using var after = await factory.CreateDbContextAsync();
        var stampAfter = await after.Users.Where(u => u.Id == targetId).Select(u => u.SecurityStamp).SingleAsync();
        Assert.NotEqual(stampBefore, stampAfter);
    }

    // Throws on the Nth SaveChangesAsync call on this context (1-based) - mirrors PurchasingServiceTests/
    // MaterialOrderServiceTests' SaveCountingContext, but injects a failure instead of just counting.
    private sealed class ThrowOnNthSaveContext(DbContextOptions<FurniturePlannerContext> options, int throwOnCall) : FurniturePlannerContext(options)
    {
        private int _saveCount;
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCount++;
            if (_saveCount == throwOnCall) { throw new DbUpdateException("injected failure"); }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class ThrowOnNthSaveContextFactory(DbContextOptions<FurniturePlannerContext> options, int throwOnCall) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new ThrowOnNthSaveContext(options, throwOnCall);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    // CreateAsync writes the user (SaveChanges #1) then its roles (SaveChanges #2) - a failure on the
    // second write must roll the whole thing back through the shared transaction, not leave a
    // roleless user row behind for SetRolesAsync to quietly "fix" later.
    [Fact]
    public async Task Create_RoleWriteFails_RollsBackTheUserRow()
    {
        var (baseFactory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        var faultyFactory = new ThrowOnNthSaveContextFactory(options, throwOnCall: 2);
        var service = new UserAdminService(faultyFactory, new PasswordHasher<FurnitureUser>());

        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync("jdoe", "John", "Doe", "secret1", [Roles.Office]));

        await using var db = await baseFactory.CreateDbContextAsync();
        Assert.False(await db.Users.AnyAsync(u => u.UserName == "jdoe"));
    }

    // Intercepts SetRolesAsync(admin1)'s own SaveChangesAsync and, before it runs, races a second
    // SetRolesAsync(admin2) call through on the same connection - admin1's guard already read admin2
    // as cover (admin1's own removal hasn't committed yet), so the racer's guard also reads admin1 as
    // cover and would happily strip admin2 too if nothing stopped it, leaving zero active admins.
    // Mirrors MaterialOrderServiceTests/MaterialNeedsServiceTests' SeedBeforeFirstSaveContext.
    private sealed class SeedBeforeFirstSaveContext(DbContextOptions<FurniturePlannerContext> options, Func<Task> seedBeforeFirstSave) : FurniturePlannerContext(options)
    {
        private bool _seeded;
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_seeded) { _seeded = true; await seedBeforeFirstSave(); }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class SeedBeforeFirstSaveContextFactory(DbContextOptions<FurniturePlannerContext> options, Func<Task> seedBeforeFirstSave) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new SeedBeforeFirstSaveContext(options, seedBeforeFirstSave);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    // Task 4b: SetRolesAsync's last-admin guard and its role write were never in the same transaction
    // (unlike DeactivateAsync, already fixed in Task 4) - two racing calls could each read the other
    // as cover and both proceed, leaving zero active admins. The BeginTransactionAsync wrap (the
    // DeactivateAsync idiom) makes the racer's own transaction attempt on the same connection fail
    // outright instead of silently succeeding - either outcome per call is acceptable here, the
    // invariant that must hold is that at least one admin survives the race.
    [Fact]
    public async Task SetRoles_RacesLastAdminGuard_NeverLeavesZeroActiveAdmins()
    {
        var (baseFactory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var setup = new UserAdminService(baseFactory, new PasswordHasher<FurnitureUser>());
        await setup.CreateAsync("admin1", "A", "One", "secret1", [Roles.Admin]);
        await setup.CreateAsync("admin2", "A", "Two", "secret1", [Roles.Admin]);
        var admin1Id = (await setup.ListAsync()).Single(u => u.UserName == "admin1").Id;
        var admin2Id = (await setup.ListAsync()).Single(u => u.UserName == "admin2").Id;

        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        var raceFactory = new SeedBeforeFirstSaveContextFactory(options, async () =>
        {
            var racer = new UserAdminService(new TestDbContextFactory(options), new PasswordHasher<FurnitureUser>());
            try { await racer.SetRolesAsync(admin2Id, [Roles.Office]); } catch { /* a rejected race is fine - see assertion */ }
        });
        var racingService = new UserAdminService(raceFactory, new PasswordHasher<FurnitureUser>());

        try { await racingService.SetRolesAsync(admin1Id, [Roles.Office]); } catch { /* a rejected race is fine - see assertion */ }

        var activeAdmins = (await setup.ListAsync()).Count(u => u.Roles.Contains(Roles.Admin) && !u.IsDeactivated);
        Assert.True(activeAdmins >= 1, "the last-admin guard must never let a race strip every admin");
    }

    [Fact]
    public async Task ResetPassword_Verifies_AndRejectsShortPassword()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var hasher = new PasswordHasher<FurnitureUser>();
        var service = new UserAdminService(factory, hasher);
        await service.CreateAsync("resetme", "R", "M", "secret1", [Roles.Office]);
        var userId = (await service.ListAsync()).Single().Id;

        await service.ResetPasswordAsync(userId, "newsecret");

        await using var db = await factory.CreateDbContextAsync();
        var stored = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(stored, stored.PasswordHash!, "newsecret"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetPasswordAsync(userId, "abc"));
    }
}
