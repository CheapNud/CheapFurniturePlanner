using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors UserAdminServiceTests: in-memory SQLite, migrated schema.
public class ServiceTicketServiceTests
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

    private static ServiceTicketService NewService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser who) => new(factory, who);
    private static readonly FakeCurrentUser OfficeUser = new("office-1", Roles.Office);

    private static async Task<int> SeedConsumerAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var consumer = new Consumer { Name = "Jansen" };
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        return consumer.Id;
    }

    [Fact]
    public async Task Create_NumbersSequentially_LogsAndStampsCreator()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = NewService(factory, OfficeUser);
        var consumerId = await SeedConsumerAsync(factory);

        var first = await service.CreateTicketAsync(consumerId, null, "seat sags", "Main St 1", ServiceFlow.Undecided, [new ServiceLineInput(null, "left seat")]);
        var second = await service.CreateTicketAsync(consumerId, null, "arm loose", null, ServiceFlow.Undecided, []);

        Assert.Equal($"SRV-{DateTime.UtcNow.Year}-0001", first.TicketNumber);
        Assert.Equal($"SRV-{DateTime.UtcNow.Year}-0002", second.TicketNumber);
        var loaded = await service.GetAsync(first.Id);
        Assert.Equal("office-1", loaded!.CreatedByUserId);
        Assert.Equal(ServiceTicketState.New, loaded.State);
        Assert.Single(loaded.Lines);
        var log = Assert.Single(loaded.Logs);
        Assert.Equal("office-1", log.UserId);
    }

    [Fact]
    public async Task Create_WithFlow_CreatesTypedRow()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = NewService(factory, OfficeUser);
        var consumerId = await SeedConsumerAsync(factory);

        var ticket = await service.CreateTicketAsync(consumerId, null, "x", null, ServiceFlow.Internal, []);

        var loaded = await service.GetAsync(ticket.Id);
        Assert.Equal(ServiceFlow.Internal, loaded!.Flow);
        Assert.NotNull(loaded.InternalRepair);
        Assert.Null(loaded.SupplierReport);
    }

    [Fact]
    public async Task Create_AsMechanic_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = NewService(factory, new FakeCurrentUser("wrench", Roles.Mechanic));
        var consumerId = await SeedConsumerAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTicketAsync(consumerId, null, "x", null, ServiceFlow.Undecided, []));
    }

    [Fact]
    public async Task SetFlow_WhileNew_SwitchesTypedRow_AndLogs()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = NewService(factory, OfficeUser);
        var consumerId = await SeedConsumerAsync(factory);
        var ticket = await service.CreateTicketAsync(consumerId, null, "x", null, ServiceFlow.Internal, []);

        await service.SetFlowAsync(ticket.Id, ServiceFlow.External);

        var loaded = await service.GetAsync(ticket.Id);
        Assert.Equal(ServiceFlow.External, loaded!.Flow);
        Assert.Null(loaded.InternalRepair);
        Assert.NotNull(loaded.SupplierReport);
        Assert.Contains(loaded.Logs, l => l.Message.Contains("External"));
    }

    [Fact]
    public async Task SetFlow_OnceInProgress_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = NewService(factory, OfficeUser);
        var consumerId = await SeedConsumerAsync(factory);
        var ticket = await service.CreateTicketAsync(consumerId, null, "x", null, ServiceFlow.External, []);
        await service.StartAsync(ticket.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetFlowAsync(ticket.Id, ServiceFlow.Internal));
    }

    [Fact]
    public async Task Start_RequiresFlow_AndTransitions()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = NewService(factory, OfficeUser);
        var consumerId = await SeedConsumerAsync(factory);
        var undecided = await service.CreateTicketAsync(consumerId, null, "x", null, ServiceFlow.Undecided, []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(undecided.Id));

        var external = await service.CreateTicketAsync(consumerId, null, "y", null, ServiceFlow.External, []);
        await service.StartAsync(external.Id);
        Assert.Equal(ServiceTicketState.InProgress, (await service.GetAsync(external.Id))!.State);
    }

    [Fact]
    public async Task Cancel_FromNewAndInProgress_ButNotTwice()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = NewService(factory, OfficeUser);
        var consumerId = await SeedConsumerAsync(factory);
        var ticket = await service.CreateTicketAsync(consumerId, null, "x", null, ServiceFlow.External, []);
        await service.StartAsync(ticket.Id);

        await service.CancelAsync(ticket.Id, "consumer withdrew");

        var loaded = await service.GetAsync(ticket.Id);
        Assert.Equal(ServiceTicketState.Cancelled, loaded!.State);
        Assert.Equal("consumer withdrew", loaded.CancelReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(ticket.Id, "again"));
    }

    [Fact]
    public async Task OpenTicketCount_CountsOnlyOpenTicketsForOrder()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = NewService(factory, OfficeUser);
        var consumerId = await SeedConsumerAsync(factory);
        int orderId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var seller = new Seller { Name = "Shop", Multiplier = 1m };
            db.Sellers.Add(seller);
            await db.SaveChangesAsync();
            var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumerId, MarketCode = "BE" };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }
        var open = await service.CreateTicketAsync(consumerId, orderId, "x", null, ServiceFlow.Undecided, []);
        var cancelled = await service.CreateTicketAsync(consumerId, orderId, "y", null, ServiceFlow.Undecided, []);
        await service.CancelAsync(cancelled.Id, "dupe");

        Assert.Equal(1, await service.OpenTicketCountForOrderAsync(orderId));
    }
}
