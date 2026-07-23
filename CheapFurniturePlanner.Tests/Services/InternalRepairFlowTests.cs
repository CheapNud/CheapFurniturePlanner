using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Internal repair flow (Task 3): dispatch a mechanic, record execution, set outcome, and roll up
// mileage. SQLite harness mirrors UserAdminServiceTests (roles seeded - SeedMechanicAsync needs
// the Mechanic role row to exist for DispatchAsync's role check).
public class InternalRepairFlowTests
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

    private static async Task<string> SeedMechanicAsync(IDbContextFactory<FurniturePlannerContext> factory, string userName = "wrench")
    {
        var admin = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await admin.CreateAsync(userName, "Mech", "Anic", "secret1", [Roles.Mechanic]);
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync();
    }

    private static async Task<ServiceTicket> SeedInternalTicketAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var service = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Consumers.Add(new Consumer { Name = "Jansen" });
            await db.SaveChangesAsync();
        }
        await using var db2 = await factory.CreateDbContextAsync();
        var consumerId = await db2.Consumers.Select(c => c.Id).FirstAsync();
        return await service.CreateTicketAsync(consumerId, null, "seat sags", null, ServiceFlow.Internal, []);
    }

    [Fact]
    public async Task Dispatch_AssignsMechanic_TransitionsToInProgress_AndLogs()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var mechanicId = await SeedMechanicAsync(factory);
        var ticket = await SeedInternalTicketAsync(factory);
        var service = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));

        await service.DispatchAsync(ticket.Id, mechanicId, new DateTime(2026, 8, 1));

        var loaded = await service.GetAsync(ticket.Id);
        Assert.Equal(ServiceTicketState.InProgress, loaded!.State);
        Assert.Equal(mechanicId, loaded.InternalRepair!.AssignedUserId);
        Assert.Equal(new DateTime(2026, 8, 1), loaded.InternalRepair.ExecutionDate);
        Assert.Contains(loaded.Logs, l => l.Message.StartsWith("Assigned to"));
    }

    [Fact]
    public async Task Dispatch_NonMechanicAssignee_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var ticket = await SeedInternalTicketAsync(factory);
        var admin = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await admin.CreateAsync("clerk", "Off", "Ice", "secret1", [Roles.Office]);
        await using var db = await factory.CreateDbContextAsync();
        var clerkId = await db.Users.Where(u => u.UserName == "clerk").Select(u => u.Id).SingleAsync();
        var service = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DispatchAsync(ticket.Id, clerkId, null));
    }

    [Fact]
    public async Task RecordExecution_ByAssignedMechanic_Persists()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var mechanicId = await SeedMechanicAsync(factory);
        var ticket = await SeedInternalTicketAsync(factory);
        var office = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        await office.DispatchAsync(ticket.Id, mechanicId, null);

        var mechanic = new ServiceTicketService(factory, new FakeCurrentUser(mechanicId, Roles.Mechanic));
        await mechanic.RecordExecutionAsync(ticket.Id, new TimeSpan(9, 0, 0), new TimeSpan(10, 30, 0), 100, 140, "tightened frame", "brought spare webbing");

        var loaded = await office.GetAsync(ticket.Id);
        var repair = loaded!.InternalRepair!;
        Assert.Equal(new TimeSpan(1, 30, 0), repair.DepartureTime - repair.ArrivalTime);
        Assert.Equal(40, repair.MileageAfter - repair.MileageBefore);
        Assert.Equal("tightened frame", repair.SolutionDescription);
    }

    [Fact]
    public async Task RecordExecution_ByUnassignedMechanic_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var mechanicId = await SeedMechanicAsync(factory);
        var otherId = await SeedMechanicAsync(factory, "wrench2");
        var ticket = await SeedInternalTicketAsync(factory);
        var office = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        await office.DispatchAsync(ticket.Id, mechanicId, null);

        var other = new ServiceTicketService(factory, new FakeCurrentUser(otherId, Roles.Mechanic));
        await Assert.ThrowsAsync<InvalidOperationException>(() => other.RecordExecutionAsync(ticket.Id, null, null, null, null, "not mine", null));
    }

    [Fact]
    public async Task Outcome_Resolved_ResolvesTicket_OtherOutcomesLeaveInProgress()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var mechanicId = await SeedMechanicAsync(factory);
        var ticket = await SeedInternalTicketAsync(factory);
        var office = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        await office.DispatchAsync(ticket.Id, mechanicId, null);
        var mechanic = new ServiceTicketService(factory, new FakeCurrentUser(mechanicId, Roles.Mechanic));

        await mechanic.SetOutcomeAsync(ticket.Id, RepairOutcome.NotResolved);
        Assert.Equal(ServiceTicketState.InProgress, (await office.GetAsync(ticket.Id))!.State);

        await mechanic.SetOutcomeAsync(ticket.Id, RepairOutcome.Resolved);
        var loaded = await office.GetAsync(ticket.Id);
        Assert.Equal(ServiceTicketState.Resolved, loaded!.State);
        Assert.NotNull(loaded.ResolvedAt);
    }

    [Fact]
    public async Task MechanicMileageTotal_SumsAcrossRepairs()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var mechanicId = await SeedMechanicAsync(factory);
        var office = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var mechanic = new ServiceTicketService(factory, new FakeCurrentUser(mechanicId, Roles.Mechanic));
        var first = await SeedInternalTicketAsync(factory);
        await office.DispatchAsync(first.Id, mechanicId, null);
        await mechanic.RecordExecutionAsync(first.Id, null, null, 100, 140, null, null);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var consumerId = await db.Consumers.Select(c => c.Id).FirstAsync();
            var second = await office.CreateTicketAsync(consumerId, null, "second visit", null, ServiceFlow.Internal, []);
            await office.DispatchAsync(second.Id, mechanicId, null);
            await mechanic.RecordExecutionAsync(second.Id, null, null, 200, 260, null, null);
        }

        Assert.Equal(100, await office.MechanicMileageTotalAsync(mechanicId));
    }
}
