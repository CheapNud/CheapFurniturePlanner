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

// Task 4: load-list PDF for a trip. SQLite harness mirrors PlanningQueriesTests; PDF assertion
// mirrors SupplierReportFlowTests' GeneratePdf fact (real PdfExportService, temp root, iText
// text extraction).
public class TripLoadListPdfTests
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

    private static readonly FakeCurrentUser OfficeUser = new("office-1", Roles.Office);

    // Seeds a Seller/Consumer/Order with a delivery address in the given region, promised the
    // given date, one deliver-to-warehouse line of quantity 1. Returns the order id.
    private static async Task<int> SeedOrderAsync(IDbContextFactory<FurniturePlannerContext> factory,
        string street, int regionId, DateTime? promisedDeliveryDate)
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop", Multiplier = 1m };
        var consumer = new Consumer { Name = "Jansen" };
        var address = new Address { Street = street, Number = "1", PostalCode = "1000", City = "Brussel", RegionId = regionId };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        db.Addresses.Add(address);
        await db.SaveChangesAsync();
        var order = new Order
        {
            OrderNumber = $"ORD-2026-{await db.Orders.CountAsync() + 1:D4}",
            SellerId = seller.Id,
            ConsumerId = consumer.Id,
            MarketCode = "BE",
            State = OrderState.Placed,
            DeliveryAddressId = address.Id,
            PromisedDeliveryDate = promisedDeliveryDate,
        };
        order.Lines.Add(new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 1, DeliverToWarehouse = true });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task LoadList_ContainsHeaderRowsUnitsInPositionOrder_AndPromiseMarker()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        int northRegionId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var region = new Region { Code = "NORTH", Name = "North" };
            db.Regions.Add(region);
            await db.SaveChangesAsync();
            northRegionId = region.Id;
        }

        var units = new ProductionUnitService(factory, OfficeUser);
        var orderAId = await SeedOrderAsync(factory, "Missed Street", northRegionId, new DateTime(2026, 8, 15));
        var orderBId = await SeedOrderAsync(factory, "OnTime Street", northRegionId, null);
        await units.SpawnForOrderAsync(orderAId);
        await units.SpawnForOrderAsync(orderBId);

        var trip = await units.CreateTripAsync();
        await units.UpdateTripAsync(trip.Id, new DateTime(2026, 8, 20), "Truck 1", "Driver Dan", northRegionId);

        string unitACode, unitBCode;
        int unitAId, unitBId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var unitA = await db.ProductionUnits.SingleAsync(u => u.OrderId == orderAId);
            var unitB = await db.ProductionUnits.SingleAsync(u => u.OrderId == orderBId);
            unitA.State = ProductionUnitState.Arrived;
            unitB.State = ProductionUnitState.Arrived;
            unitACode = unitA.UnitCode;
            unitBCode = unitB.UnitCode;
            unitAId = unitA.Id;
            unitBId = unitB.Id;
            await db.SaveChangesAsync();
        }

        // Insert in reverse of intended load order: unit A gets position 2, unit B gets position 1.
        await units.AssignToTripAsync(trip.Id, unitAId);
        await units.AssignToTripAsync(trip.Id, unitBId);
        await units.SetLoadPositionAsync(unitAId, 2);
        await units.SetLoadPositionAsync(unitBId, 1);

        var outputRoot = Path.Combine(Path.GetTempPath(), "dp1-pdf-tests", Guid.NewGuid().ToString("N"));
        var pdf = new TripLoadListPdf(factory, new PdfExportService(new PdfTemplateService()), outputRoot);
        var filePath = await pdf.GenerateAsync(trip.Id);

        Assert.True(new FileInfo(filePath).Length > 0);
        using var readerDoc = new PdfDocument(new PdfReader(filePath));
        var pageText = PdfTextExtractor.GetTextFromPage(readerDoc.GetFirstPage());

        Assert.Contains(trip.TripCode, pageText);
        Assert.Contains(unitACode, pageText);
        Assert.Contains(unitBCode, pageText);
        Assert.Contains("OnTime Street", pageText);
        Assert.Contains("2026-08-15 !", pageText);
        Assert.True(pageText.IndexOf(unitBCode, StringComparison.Ordinal) < pageText.IndexOf(unitACode, StringComparison.Ordinal),
            "position-1 unit should appear before position-2 unit");
    }
}
