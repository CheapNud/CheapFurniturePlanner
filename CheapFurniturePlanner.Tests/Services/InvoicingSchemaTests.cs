using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors ServiceTicketSchemaTests: in-memory SQLite, migrated schema, roles seeded.
public class InvoicingSchemaTests
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
    public async Task Invoice_WithLinesAndCredit_RoundTrips_AndOrderIsUniquelyInvoiced()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        int orderId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var seller = new Seller { Name = "Shop" };
            var consumer = new Consumer { Name = "Jansen", VatNumber = "BE0123456789" };
            db.Sellers.Add(seller);
            db.Consumers.Add(consumer);
            await db.SaveChangesAsync();
            var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = "BE", State = OrderState.Placed };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
            var invoice = new Invoice { InvoiceNumber = "INV-2026-0001", OrderId = orderId, IssuedAt = DateTime.UtcNow, DueDate = DateTime.UtcNow.AddDays(30), NetTotal = 81m, VatTotal = 17.01m, GrossTotal = 98.01m, CreatedByUserId = "u1" };
            invoice.Lines.Add(new InvoiceLine { OrderLineId = 1, Description = "K7E:OAK", Quantity = 1, UnitPrice = 100m, DiscountPercent = 10m, LineTotal = 90m, VatRatePercent = 21m, VatAmount = 17.01m });
            invoice.CreditNotes.Add(new CreditNote { CreditNoteNumber = "CN-2026-0001", Reason = CreditReason.Goodwill, NetAmount = 10m, VatAmount = 2.1m, GrossAmount = 12.1m, IssuedAt = DateTime.UtcNow, CreatedByUserId = "u1" });
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var loaded = await db.Invoices.Include(i => i.Lines).Include(i => i.CreditNotes).SingleAsync();
            Assert.Equal(98.01m, loaded.GrossTotal);
            Assert.Single(loaded.Lines);
            Assert.Equal(CreditReason.Goodwill, Assert.Single(loaded.CreditNotes).Reason);
            var storedConsumer = await db.Consumers.SingleAsync();
            Assert.Equal("BE0123456789", storedConsumer.VatNumber);

            db.Invoices.Add(new Invoice { InvoiceNumber = "INV-2026-0002", OrderId = loaded.OrderId, IssuedAt = DateTime.UtcNow, DueDate = DateTime.UtcNow, CreatedByUserId = "u1" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); // unique OrderId index
        }
    }

    [Fact]
    public async Task MarketVatRate_UniquePerMarket()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using var db = await factory.CreateDbContextAsync();
        db.MarketVatRates.Add(new MarketVatRate { MarketCode = "BE", RatePercent = 21m });
        db.MarketVatRates.Add(new MarketVatRate { MarketCode = "BE", RatePercent = 6m });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
