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
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 6: /service lists tickets via ServiceTicketService.ListAsync, scoped by role - Admin/Office
// see everything plus the "New ticket" button, Mechanics are pinned to their own assigned tickets
// and never see the button. Harness mirrors UsersPageTests (bUnit + in-memory SQLite).
public class ServiceListPageTests : TestContext
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

    private static async Task<int> SeedConsumerAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var consumer = new Consumer { Name = "Jansen" };
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        return consumer.Id;
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser who)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(who);
        Services.AddSingleton(sp => new ServiceTicketService(factory, who));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task Office_SeesAllTickets_AndNewButton()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var seedingService = new ServiceTicketService(factory, office);
        var consumerId = await SeedConsumerAsync(factory);
        await seedingService.CreateTicketAsync(consumerId, null, "seat sags", "Main St 1", ServiceFlow.Undecided, []);
        await seedingService.CreateTicketAsync(consumerId, null, "arm loose", null, ServiceFlow.Undecided, []);
        ConfigureServices(factory, office);

        var cut = Render<ServiceListPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("SRV-", cut.Markup);
            Assert.Contains("New ticket", cut.Markup);
        });
    }

    [Fact]
    public async Task Mechanic_SeesOnlyAssignedTickets_NoNewButton()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var seedingService = new ServiceTicketService(factory, office);
        var admin = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await admin.CreateAsync("wrench", "Mo", "Mechanic", "secret1", [Roles.Mechanic]);
        await using var userDb = await factory.CreateDbContextAsync();
        var mechanicId = await userDb.Users.Where(u => u.UserName == "wrench").Select(u => u.Id).SingleAsync();
        var consumerId = await SeedConsumerAsync(factory);

        var assigned = await seedingService.CreateTicketAsync(consumerId, null, "seat sags", null, ServiceFlow.Internal, []);
        await seedingService.DispatchAsync(assigned.Id, mechanicId, null);
        var unassigned = await seedingService.CreateTicketAsync(consumerId, null, "arm loose", null, ServiceFlow.Internal, []);
        Assert.Equal($"SRV-{DateTime.UtcNow.Year}-0001", assigned.TicketNumber);
        Assert.Equal($"SRV-{DateTime.UtcNow.Year}-0002", unassigned.TicketNumber);

        ConfigureServices(factory, new FakeCurrentUser(mechanicId, Roles.Mechanic));

        var cut = Render<ServiceListPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(assigned.TicketNumber, cut.Markup);        // the assigned one
            Assert.DoesNotContain(unassigned.TicketNumber, cut.Markup); // the unassigned one
            Assert.DoesNotContain("New ticket", cut.Markup);
        });
    }
}
