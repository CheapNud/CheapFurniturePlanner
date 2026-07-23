using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using CheapHelpers.Services.DataExchange.Pdf;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors ServiceTicketServiceTests: in-memory SQLite, migrated schema.
public class SupplierReportFlowTests
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

    private static async Task<(int ConsumerId, int OrderId, int DropshipLineId)> SeedDropshipOrderAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var consumer = new Consumer { Name = "Jansen" };
        var seller = new Seller { Name = "Shop", Multiplier = 1m };
        db.Consumers.Add(consumer);
        db.Sellers.Add(seller);
        await db.SaveChangesAsync();
        var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = "BE" };
        order.Lines.Add(new OrderLine { Kind = OrderLineKind.StandaloneArticle, DisplayIndex = 1, Quantity = 1, UnitPrice = 100m, LineTotal = 100m, SupplierRef = "LAMPCO-77" });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (consumer.Id, order.Id, order.Lines[0].Id);
    }

    [Fact]
    public async Task ExternalFlow_PrefillsSupplierRefFromDropshipLine()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var (consumerId, orderId, lineId) = await SeedDropshipOrderAsync(factory);
        var service = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));

        var ticket = await service.CreateTicketAsync(consumerId, orderId, "lamp flickers", null, ServiceFlow.External, [new ServiceLineInput(lineId, "the lamp")]);

        var loaded = await service.GetAsync(ticket.Id);
        Assert.Equal("LAMPCO-77", loaded!.SupplierReport!.SupplierRef);
    }

    [Fact]
    public async Task MarkReported_StampsReportedAt_StartsTicket_AndLogs()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var (consumerId, orderId, lineId) = await SeedDropshipOrderAsync(factory);
        var service = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var ticket = await service.CreateTicketAsync(consumerId, orderId, "lamp flickers", null, ServiceFlow.External, [new ServiceLineInput(lineId, "the lamp")]);

        await service.MarkReportedAsync(ticket.Id);

        var loaded = await service.GetAsync(ticket.Id);
        Assert.NotNull(loaded!.SupplierReport!.ReportedAt);
        Assert.Equal(ServiceTicketState.InProgress, loaded.State);
        Assert.Contains(loaded.Logs, l => l.Message == "Report generated");
    }

    [Fact]
    public async Task MarkReported_WithoutSupplierRef_Throws()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        await using (var db = await factory.CreateDbContextAsync()) { db.Consumers.Add(new Consumer { Name = "Jansen" }); await db.SaveChangesAsync(); }
        await using var db2 = await factory.CreateDbContextAsync();
        var consumerId = await db2.Consumers.Select(c => c.Id).FirstAsync();
        var ticket = await service.CreateTicketAsync(consumerId, null, "x", null, ServiceFlow.External, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkReportedAsync(ticket.Id));
    }

    [Fact]
    public async Task Decision_ResolvesTicket()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var (consumerId, orderId, lineId) = await SeedDropshipOrderAsync(factory);
        var service = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var ticket = await service.CreateTicketAsync(consumerId, orderId, "lamp flickers", null, ServiceFlow.External, [new ServiceLineInput(lineId, "the lamp")]);
        await service.MarkReportedAsync(ticket.Id);

        await service.SetDecisionAsync(ticket.Id, "replacement shipped", "case 4711");

        var loaded = await service.GetAsync(ticket.Id);
        Assert.Equal(ServiceTicketState.Resolved, loaded!.State);
        Assert.Equal("replacement shipped", loaded.SupplierReport!.Decision);
    }

    [Fact]
    public async Task GeneratePdf_WritesFileContainingTicketNumber()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var (consumerId, orderId, lineId) = await SeedDropshipOrderAsync(factory);
        var service = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var ticket = await service.CreateTicketAsync(consumerId, orderId, "lamp flickers", "Main St 1", ServiceFlow.External, [new ServiceLineInput(lineId, "the lamp")]);

        var outputRoot = Path.Combine(Path.GetTempPath(), "sv1-pdf-tests", Guid.NewGuid().ToString("N"));
        var pdf = new SupplierReportPdf(factory, new PdfExportService(new PdfTemplateService()), outputRoot);
        var filePath = await pdf.GenerateAsync(ticket.Id);

        Assert.True(new FileInfo(filePath).Length > 0);
        using var readerDoc = new PdfDocument(new PdfReader(filePath));
        var pageText = PdfTextExtractor.GetTextFromPage(readerDoc.GetFirstPage());
        Assert.Contains(ticket.TicketNumber, pageText);
        Assert.Contains("LAMPCO-77", pageText);
    }
}
