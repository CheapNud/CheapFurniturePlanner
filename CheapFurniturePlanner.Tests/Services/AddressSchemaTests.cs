using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors ServiceTicketSchemaTests: in-memory SQLite, migrated schema, roles seeded.
public class AddressSchemaTests
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
    public async Task Address_Region_Supplier_Book_RoundTrip_WithRestrictAndSetNull()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;

        int addressId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var region = new Region { Code = "NORTH", Name = "North route" };
            db.Regions.Add(region);
            await db.SaveChangesAsync();

            var homeAddress = new Address { Street = "Main Street", Number = "12", Box = "B", PostalCode = "1000", City = "Springfield", RegionId = region.Id };
            var consumer = new Consumer { Name = "Jansen", PrimaryAddress = homeAddress };
            db.Consumers.Add(consumer);
            var supplier = new Supplier { Code = "LAMPCO", Name = "Lampco", Address = new Address { Street = "Dock Road", Number = "3", PostalCode = "2000", City = "Harborville" } };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            addressId = homeAddress.Id;

            db.ConsumerDeliveryAddresses.Add(new ConsumerDeliveryAddress { ConsumerId = consumer.Id, AddressId = homeAddress.Id, Label = "Home", IsDefault = true });
            await db.SaveChangesAsync();

            // Region delete -> Address.RegionId nulls (SetNull)
            db.Regions.Remove(region);
            await db.SaveChangesAsync();
            Assert.Null((await db.Addresses.AsNoTracking().FirstAsync(a => a.Id == addressId)).RegionId);
        }

        // Address delete blocked while referenced (Restrict). Fresh context: the tracked
        // ConsumerDeliveryAddress dependent from above would otherwise trip EF's client-side
        // required-relationship check before the statement ever reaches SQLite's FK enforcement.
        await using (var freshDb = await factory.CreateDbContextAsync())
        {
            freshDb.Addresses.Remove(await freshDb.Addresses.FirstAsync(a => a.Id == addressId));
            await Assert.ThrowsAsync<DbUpdateException>(() => freshDb.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task SupplierCode_Unique_And_LineFkRoundTrips()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;

        var supplier = new Supplier { Code = "LAMPCO", Name = "Lampco" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();

            db.Suppliers.Add(new Supplier { Code = "LAMPCO", Name = "Duplicate" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Consumers.Add(new Consumer { Id = 1, Name = "Jansen" });
            db.Sellers.Add(new Seller { Id = 1, Name = "Acme" });
            var order = new Order { OrderNumber = "ORD-1", SellerId = 1, ConsumerId = 1, MarketCode = "BE", CreatedAt = DateTime.UtcNow };
            order.Lines.Add(new OrderLine { DisplayIndex = 1, Kind = OrderLineKind.StandaloneArticle, SupplierId = supplier.Id });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var loadedLine = await db.OrderLines.Include(l => l.Supplier).SingleAsync();
            Assert.NotNull(loadedLine.Supplier);
            Assert.Equal("LAMPCO", loadedLine.Supplier!.Code);
        }
    }

    // HD1 backstop: the filtered unique index (ConsumerId) WHERE IsDefault = 1 rejects a second
    // default row for the same consumer even when inserted directly (bypassing PartyService).
    // The swap itself (PartyService.SetDefaultDeliveryAddressAsync's clear-siblings-then-set, now
    // wrapped in a transaction) is covered end-to-end through the real service by
    // PartyAddressTests.Book_FirstAutoDefaults_SetDefaultClears_RemoveGuards - a hand-rolled repro
    // against the raw DbContext here would depend on EF's change-tracker flush order, which isn't
    // a contract worth pinning in a schema test.
    [Fact]
    public async Task ConsumerDeliveryAddress_OneDefaultPerConsumer_IndexRejectsRawDuplicate()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;

        int consumerId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var consumer = new Consumer { Name = "Jansen" };
            db.Consumers.Add(consumer);
            await db.SaveChangesAsync();
            consumerId = consumer.Id;

            db.ConsumerDeliveryAddresses.Add(new ConsumerDeliveryAddress
            {
                ConsumerId = consumerId,
                Address = new Address { Street = "A St", Number = "1", PostalCode = "1000", City = "Springfield" },
                Label = "Home",
                IsDefault = true,
            });
            await db.SaveChangesAsync();
        }

        // Raw duplicate default insert (bypassing PartyService's clear-siblings step) - rejected by
        // the filtered unique index.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ConsumerDeliveryAddresses.Add(new ConsumerDeliveryAddress
            {
                ConsumerId = consumerId,
                Address = new Address { Street = "B St", Number = "2", PostalCode = "2000", City = "Harborville" },
                Label = "Work",
                IsDefault = true,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
