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

// Harness mirrors PartiesPageTests (bUnit + in-memory SQLite). Covers the two SetupPage paths
// called out in the task brief: empty store renders the form, and the demo-admin button
// produces a user (service-level assert, matching AnyUsersAsync/CreateFirstAdminAsync in
// IdentityWiringTests).
public class SetupPageTests : TestContext
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
            RoleSeeder.SeedAsync(migrateContext).GetAwaiter().GetResult();
        }
        return (new TestDbContextFactory(options), conn);
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton<IPasswordHasher<FurnitureUser>>(new PasswordHasher<FurnitureUser>());
        Services.AddScoped(sp => new UserAdminService(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(),
            sp.GetRequiredService<IPasswordHasher<FurnitureUser>>()));
        Services.AddScoped(sp => new SetupState(
            sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(),
            sp.GetRequiredService<UserAdminService>()));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudPopoverProvider>();
        Render<MudBlazor.MudSnackbarProvider>();
    }

    [Fact]
    public void Render_EmptyStore_ShowsForm()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        ConfigureServices(factory);

        var cut = Render<SetupPage>();

        cut.WaitForAssertion(() => Assert.Contains("Create admin account", cut.Markup));
    }

    [Fact]
    public void DemoAdminButton_CreatesUser_InAdminRole()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        ConfigureServices(factory);

        var cut = Render<SetupPage>();
        var demoButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Create demo admin");
        cut.InvokeAsync(() => demoButton.Click());

        cut.WaitForAssertion(() => Assert.Contains("Demo admin created", cut.Markup));

        using var verify = factory.CreateDbContext();
        var demoUser = verify.Users.Single(u => u.UserName == "demo");
        var isAdmin = verify.UserRoles
            .Join(verify.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Any(x => x.UserId == demoUser.Id && x.Name == Roles.Admin);
        Assert.True(isAdmin);
    }
}
