using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors UserAdminServiceTests: in-memory SQLite, migrated schema, roles seeded.
public class ServiceTicketSchemaTests
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
    public async Task Ticket_WithLinesLogsPhotosAndBothFlowShapes_RoundTrips()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Consumers.Add(new Consumer { Id = 1, Name = "Jansen" });
            var ticket = new ServiceTicket
            {
                TicketNumber = "SRV-2026-0001",
                ConsumerId = 1,
                CreatedByUserId = "u1",
                CreatedAt = DateTime.UtcNow,
                ProblemDescription = "seat sags",
                Flow = ServiceFlow.Internal,
            };
            ticket.Lines.Add(new ServiceTicketLine { Description = "left seat" });
            ticket.Logs.Add(new ServiceTicketLog { At = DateTime.UtcNow, UserId = "u1", Message = "created" });
            ticket.Photos.Add(new ServiceTicketPhoto { Kind = PhotoKind.Before, FileName = "a.jpg", UploadedAt = DateTime.UtcNow, UserId = "u1" });
            ticket.InternalRepair = new InternalRepair { MileageBefore = 100, MileageAfter = 140, Outcome = RepairOutcome.NotResolved };
            db.ServiceTickets.Add(ticket);
            await db.SaveChangesAsync();
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var loaded = await db.ServiceTickets
                .Include(t => t.Lines).Include(t => t.Logs).Include(t => t.Photos)
                .Include(t => t.InternalRepair).Include(t => t.SupplierReport)
                .SingleAsync();
            Assert.Equal("SRV-2026-0001", loaded.TicketNumber);
            Assert.Equal(ServiceFlow.Internal, loaded.Flow);
            Assert.Equal(ServiceTicketState.New, loaded.State);
            Assert.Single(loaded.Lines);
            Assert.Single(loaded.Logs);
            Assert.Single(loaded.Photos);
            Assert.Equal(40, loaded.InternalRepair!.MileageAfter - loaded.InternalRepair.MileageBefore);
            Assert.Null(loaded.SupplierReport);
        }
    }

    [Fact]
    public async Task DuplicateTicketNumber_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        db.Consumers.Add(new Consumer { Id = 1, Name = "Jansen" });
        db.ServiceTickets.Add(new ServiceTicket { TicketNumber = "SRV-2026-0001", ConsumerId = 1, CreatedByUserId = "u", CreatedAt = DateTime.UtcNow, ProblemDescription = "x" });
        db.ServiceTickets.Add(new ServiceTicket { TicketNumber = "SRV-2026-0001", ConsumerId = 1, CreatedByUserId = "u", CreatedAt = DateTime.UtcNow, ProblemDescription = "y" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
