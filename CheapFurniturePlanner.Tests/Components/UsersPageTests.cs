using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Task 3: the /users admin page lists users via UserAdminService, following the PartiesPage
// P2a pattern (self-load, dialog plumbing, service-boundary guard surfaced as a Snackbar).
// Harness mirrors PartiesPageTests (bUnit + in-memory SQLite), plus role seeding + the real
// PasswordHasher since UserAdminService needs both.
public class UsersPageTests : TestContext
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

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton<IPasswordHasher<FurnitureUser>>(new PasswordHasher<FurnitureUser>());
        Services.AddSingleton(sp => new UserAdminService(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(),
            sp.GetRequiredService<IPasswordHasher<FurnitureUser>>()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        RenderComponent<MudBlazor.MudDialogProvider>();
        RenderComponent<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task Render_ListsUsers()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var admin = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await admin.CreateAsync("boss", "Alice", "Admin", "secret1", [Roles.Admin, Roles.Office]);
        await admin.CreateAsync("wrench", "Mo", "Mechanic", "secret1", [Roles.Mechanic]);
        var wrenchId = (await admin.ListAsync()).Single(u => u.UserName == "wrench").Id;
        await admin.DeactivateAsync(wrenchId);
        ConfigureServices(factory);

        var cut = RenderComponent<UsersPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alice Admin", cut.Markup);
            Assert.Contains("Mo Mechanic", cut.Markup);
            Assert.Contains("Admin", cut.Markup);
            Assert.Contains("Deactivated", cut.Markup);
        });
    }

    [Fact]
    public async Task DeactivateLastAdmin_SurfacesError()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var admin = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await admin.CreateAsync("soloadmin", "Solo", "Admin", "secret1", [Roles.Admin]);
        var adminId = (await admin.ListAsync()).Single().Id;

        // Service-boundary assert (mirrors PartiesPageTests.DeleteSellerWithOrders_ThrowsAtService):
        // the page catches this and shows a Snackbar, but the guard itself lives at the service.
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.DeactivateAsync(adminId));
        Assert.False((await admin.ListAsync()).Single(u => u.Id == adminId).IsDeactivated);
    }

    [Fact]
    public async Task CreateThroughService_AppearsInList()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var admin = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await admin.CreateAsync("newperson", "New", "Person", "secret1", [Roles.Office]);
        ConfigureServices(factory);

        var cut = RenderComponent<UsersPage>();

        cut.WaitForAssertion(() => Assert.Contains("New Person", cut.Markup));
    }
}
