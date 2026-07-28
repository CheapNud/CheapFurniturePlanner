using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 3: the one-time absorb of legacy free-text supplier refs into real Supplier rows + FKs.
// Harness mirrors SupplierReportFlowTests: in-memory SQLite, migrated schema, everything seeded
// directly via EF - this predates any PartyService/OrderEntryService involvement.
public class SupplierAbsorberTests
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
        }
        return (new TestDbContextFactory(options), connection);
    }

    [Fact]
    public async Task Absorb_CreatesSuppliersFromDistinctRefs_SetsFks_Idempotent()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        int lampcoLineOneId, lampcoLineTwoId, woodworksLineId, nullRefLineId, reportTicketId, preexistingWoodworksId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var consumer = new Consumer { Name = "Jansen" };
            var seller = new Seller { Name = "Shop", Multiplier = 1m };
            db.Consumers.Add(consumer);
            db.Sellers.Add(seller);
            var woodworks = new Supplier { Code = "WOODWORKS", Name = "Woodworks Fine" };
            db.Suppliers.Add(woodworks);
            await db.SaveChangesAsync();
            preexistingWoodworksId = woodworks.Id;

            var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = "BE" };
            order.Lines.Add(new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.StandaloneArticle, Quantity = 1, SupplierRef = "LAMPCO" });
            order.Lines.Add(new OrderLine { DisplayIndex = 1, Kind = OrderLineKind.StandaloneArticle, Quantity = 1, SupplierRef = "LAMPCO" });
            order.Lines.Add(new OrderLine { DisplayIndex = 2, Kind = OrderLineKind.StandaloneArticle, Quantity = 1, SupplierRef = "WOODWORKS" });
            order.Lines.Add(new OrderLine { DisplayIndex = 3, Kind = OrderLineKind.StandaloneArticle, Quantity = 1, SupplierRef = null });
            db.Orders.Add(order);

            var ticket = new ServiceTicket { TicketNumber = "SVC-0001", ConsumerId = consumer.Id, CreatedByUserId = "office-1", ProblemDescription = "lamp flickers" };
            db.ServiceTickets.Add(ticket);
            await db.SaveChangesAsync();
            db.SupplierReports.Add(new SupplierReport { TicketId = ticket.Id, SupplierRef = "LAMPCO" });
            await db.SaveChangesAsync();

            lampcoLineOneId = order.Lines[0].Id;
            lampcoLineTwoId = order.Lines[1].Id;
            woodworksLineId = order.Lines[2].Id;
            nullRefLineId = order.Lines[3].Id;
            reportTicketId = ticket.Id;
        }

        var absorber = new SupplierAbsorber(factory);
        await absorber.AbsorbAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var suppliers = await db.Suppliers.ToListAsync();
            Assert.Equal(2, suppliers.Count);
            var lampco = Assert.Single(suppliers, s => s.Code == "LAMPCO");
            Assert.Equal("LAMPCO", lampco.Name);
            var woodworks = Assert.Single(suppliers, s => s.Code == "WOODWORKS");
            Assert.Equal(preexistingWoodworksId, woodworks.Id);
            Assert.Equal("Woodworks Fine", woodworks.Name);

            Assert.Equal(lampco.Id, (await db.OrderLines.FindAsync(lampcoLineOneId))!.SupplierId);
            Assert.Equal(lampco.Id, (await db.OrderLines.FindAsync(lampcoLineTwoId))!.SupplierId);
            Assert.Equal(woodworks.Id, (await db.OrderLines.FindAsync(woodworksLineId))!.SupplierId);
            Assert.Null((await db.OrderLines.FindAsync(nullRefLineId))!.SupplierId);
            Assert.Equal(lampco.Id, (await db.SupplierReports.FindAsync(reportTicketId))!.SupplierId);
        }

        // Re-absorb: nothing left unmatched, so this is a pure no-op.
        await absorber.AbsorbAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var suppliers = await db.Suppliers.ToListAsync();
            Assert.Equal(2, suppliers.Count);
            var lampco = await db.Suppliers.SingleAsync(s => s.Code == "LAMPCO");
            Assert.Equal(lampco.Id, (await db.OrderLines.FindAsync(lampcoLineOneId))!.SupplierId);
            Assert.Equal(lampco.Id, (await db.OrderLines.FindAsync(lampcoLineTwoId))!.SupplierId);
        }
    }
}
