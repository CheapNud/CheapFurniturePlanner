using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 2: PartyService grows to cover regions, suppliers, addresses (seller/supplier/consumer
// primary upsert) and a consumer's delivery-address book. Harness mirrors PartyServiceTests.
public class PartyAddressTests
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

    private static PartyService NewService(IDbContextFactory<FurniturePlannerContext> factory) =>
        new(factory, new FakeCurrentUser("office-1", Roles.Office));

    [Fact]
    public async Task Regions_Crud_And_DeleteBlockedWhenReferenced()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        var region = await service.AddRegionAsync("NORTH", "North");
        Assert.Equal("NORTH", region.Code);
        Assert.Equal("North", region.Name);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRegionAsync("NORTH", "Duplicate"));

        await service.UpdateRegionAsync(region.Id, "NORTH", "Noord");
        var renamed = Assert.Single(await service.RegionsAsync());
        Assert.Equal("Noord", renamed.Name);

        var unreferenced = await service.AddRegionAsync("SOUTH", "South");
        await service.DeleteRegionAsync(unreferenced.Id);
        Assert.DoesNotContain(await service.RegionsAsync(), r => r.Id == unreferenced.Id);

        var seller = await service.AddSellerAsync("RegionSeller", 1m);
        await service.SetSellerAddressAsync(seller.Id, new Address
        {
            Street = "Main St",
            Number = "1",
            PostalCode = "1000",
            City = "Brussels",
            RegionId = region.Id,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRegionAsync(region.Id));
    }

    [Fact]
    public async Task Suppliers_Crud_And_DeleteBlockedWhenReferenced()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        var supplier = await service.AddSupplierAsync("LAMPCO", "Lampco NV");
        Assert.Equal("LAMPCO", supplier.Code);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddSupplierAsync("LAMPCO", "Duplicate"));

        var deletable = await service.AddSupplierAsync("DELME", "Delete Me");
        await service.DeleteSupplierAsync(deletable.Id);
        Assert.DoesNotContain(await service.SuppliersAsync(), s => s.Id == deletable.Id);

        var seller = await service.AddSellerAsync("Seller1", 1m);
        var consumer = await service.AddConsumerAsync("Consumer1", null);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var order = new Order
            {
                OrderNumber = "ORD-1",
                SellerId = seller.Id,
                ConsumerId = consumer.Id,
                MarketCode = "EUW",
                CreatedAt = DateTime.UtcNow,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            db.OrderLines.Add(new OrderLine
            {
                OrderId = order.Id,
                DisplayIndex = 1,
                Kind = OrderLineKind.StandaloneArticle,
                SupplierId = supplier.Id,
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteSupplierAsync(supplier.Id));
    }

    [Fact]
    public async Task SellerAndSupplierAndPrimary_AddressUpsert_UpdatesInPlace()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        var seller = await service.AddSellerAsync("Seller1", 1m);
        var supplier = await service.AddSupplierAsync("SUP1", "Supplier One");
        var consumer = await service.AddConsumerAsync("Consumer1", null);

        await service.SetSellerAddressAsync(seller.Id, new Address { Street = "First St", Number = "1", PostalCode = "1000", City = "Brussels" });
        await service.SetSupplierAddressAsync(supplier.Id, new Address { Street = "First St", Number = "1", PostalCode = "1000", City = "Brussels" });
        await service.SetConsumerPrimaryAddressAsync(consumer.Id, new Address { Street = "First St", Number = "1", PostalCode = "1000", City = "Brussels" });

        int sellerAddressId, supplierAddressId, consumerAddressId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            sellerAddressId = (await db.Sellers.AsNoTracking().FirstAsync(s => s.Id == seller.Id)).AddressId!.Value;
            supplierAddressId = (await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplier.Id)).AddressId!.Value;
            consumerAddressId = (await db.Consumers.AsNoTracking().FirstAsync(c => c.Id == consumer.Id)).PrimaryAddressId!.Value;
        }

        // Upsert again with new street values - same AddressId, new values.
        await service.SetSellerAddressAsync(seller.Id, new Address { Street = "Second St", Number = "2", PostalCode = "2000", City = "Ghent" });
        await service.SetSupplierAddressAsync(supplier.Id, new Address { Street = "Second St", Number = "2", PostalCode = "2000", City = "Ghent" });
        await service.SetConsumerPrimaryAddressAsync(consumer.Id, new Address { Street = "Second St", Number = "2", PostalCode = "2000", City = "Ghent" });

        await using (var db = await factory.CreateDbContextAsync())
        {
            var sellerRow = await db.Sellers.AsNoTracking().Include(s => s.Address).FirstAsync(s => s.Id == seller.Id);
            Assert.Equal(sellerAddressId, sellerRow.AddressId);
            Assert.Equal("Second St", sellerRow.Address!.Street);
            Assert.Equal("Ghent", sellerRow.Address.City);

            var supplierRow = await db.Suppliers.AsNoTracking().Include(s => s.Address).FirstAsync(s => s.Id == supplier.Id);
            Assert.Equal(supplierAddressId, supplierRow.AddressId);
            Assert.Equal("Second St", supplierRow.Address!.Street);

            var consumerRow = await db.Consumers.AsNoTracking().Include(c => c.PrimaryAddress).FirstAsync(c => c.Id == consumer.Id);
            Assert.Equal(consumerAddressId, consumerRow.PrimaryAddressId);
            Assert.Equal("Second St", consumerRow.PrimaryAddress!.Street);
        }
    }

    [Fact]
    public async Task Book_FirstAutoDefaults_SetDefaultClears_RemoveGuards()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);
        var consumer = await service.AddConsumerAsync("Consumer1", null);

        var entryA = await service.AddDeliveryAddressAsync(consumer.Id, "Home", new Address { Street = "A St", Number = "1", PostalCode = "1000", City = "Brussels" });
        Assert.True(entryA.IsDefault);

        var entryB = await service.AddDeliveryAddressAsync(consumer.Id, "Work", new Address { Street = "B St", Number = "2", PostalCode = "2000", City = "Ghent" });
        Assert.False(entryB.IsDefault);

        await service.SetDefaultDeliveryAddressAsync(entryB.Id);
        var afterSwitch = await service.DeliveryAddressesAsync(consumer.Id);
        Assert.False(afterSwitch.Single(a => a.Id == entryA.Id).IsDefault);
        Assert.True(afterSwitch.Single(a => a.Id == entryB.Id).IsDefault);

        var stillDefaultEx = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveDeliveryAddressAsync(entryB.Id));
        Assert.Contains("default", stillDefaultEx.Message, StringComparison.OrdinalIgnoreCase);

        await service.SetDefaultDeliveryAddressAsync(entryA.Id);
        await service.RemoveDeliveryAddressAsync(entryB.Id);
        Assert.DoesNotContain(await service.DeliveryAddressesAsync(consumer.Id), a => a.Id == entryB.Id);

        var seller = await service.AddSellerAsync("Seller1", 1m);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Orders.Add(new Order
            {
                OrderNumber = "ORD-A",
                SellerId = seller.Id,
                ConsumerId = consumer.Id,
                MarketCode = "EUW",
                DeliveryAddressId = entryA.AddressId,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveDeliveryAddressAsync(entryA.Id));
    }

    // Regression: SellersAsync() must Include the Address navigation (mirrors SuppliersAsync).
    // Without it, callers like PartiesPage see Address == null for a seller that already has one,
    // the address dialog opens blank, and saving wipes the real address via ApplyAddress.
    [Fact]
    public async Task SellersAsync_IncludesAddress()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        var seller = await service.AddSellerAsync("Seller1", 1m);
        await service.SetSellerAddressAsync(seller.Id, new Address { Street = "Main St", Number = "1", PostalCode = "1000", City = "Brussels" });

        var reloaded = Assert.Single(await service.SellersAsync(), s => s.Id == seller.Id);
        Assert.NotNull(reloaded.Address);
        Assert.Equal("Main St", reloaded.Address!.Street);
    }

    // Same regression for ConsumersAsync() / PrimaryAddress (mirrors SuppliersAsync).
    [Fact]
    public async Task ConsumersAsync_IncludesPrimaryAddress()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = NewService(factory);

        var consumer = await service.AddConsumerAsync("Consumer1", null);
        await service.SetConsumerPrimaryAddressAsync(consumer.Id, new Address { Street = "Kerkstraat", Number = "1", PostalCode = "9000", City = "Gent" });

        var reloaded = Assert.Single(await service.ConsumersAsync(), c => c.Id == consumer.Id);
        Assert.NotNull(reloaded.PrimaryAddress);
        Assert.Equal("Kerkstraat", reloaded.PrimaryAddress!.Street);
    }

    [Fact]
    public async Task Guards_MechanicRejected()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new PartyService(factory, new FakeCurrentUser("mech-1", Roles.Mechanic));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRegionAsync("NORTH", "North"));
    }
}
