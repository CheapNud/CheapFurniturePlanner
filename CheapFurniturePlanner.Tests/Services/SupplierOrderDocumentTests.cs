using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using CheapHelpers.Services.DataExchange.Pdf;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Xml.Linq;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 4: PO PDF + UBL order for a Sent purchase order. SQLite harness mirrors PurchasingFlowTests;
// PDF/XML assertions mirror TripLoadListPdfTests/UblExportTests (real PdfExportService/UblService,
// temp root, iText extraction / XDocument.Load). Fixtures go through the real sweep+send flow
// (PurchasingService) and PartyService, so the PO under test is built the same way production
// code builds one.
public class SupplierOrderDocumentTests
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

    // Seeds a Seller/Consumer/placed Order with one deliver-to-warehouse configured line
    // (Quantity 2, so its two spawned units share one production identity - the grouping this
    // task adds), a Supplier with an address, and a model map routing the line's ModelCode to
    // that supplier. Returns the order id.
    private static async Task<int> SeedOrderAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop", Multiplier = 1m };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var order = new Order
        {
            OrderNumber = "ORD-2026-0001",
            SellerId = seller.Id,
            ConsumerId = consumer.Id,
            MarketCode = "BE",
            State = OrderState.Placed,
        };
        order.Lines.Add(new OrderLine
        {
            DisplayIndex = 0,
            Kind = OrderLineKind.ConfiguredElement,
            ModelCode = "FJORD",
            ElementCode = "SOFA",
            VariantCode = "V1",
            FabricColorCode = "BLU",
            DeliverToWarehouse = true,
            Quantity = 2,
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    // Full real-service chain: seed a supplier + address + model map (PartyService), seed an order
    // with two units sharing one identity (ProductionUnitService.SpawnForOrderAsync), sweep them
    // onto a draft PO and send it (PurchasingService). Returns the resulting Sent PO's id.
    private static async Task<int> SeedSentPurchaseOrderAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var parties = new PartyService(factory, OfficeUser);
        var supplier = await parties.AddSupplierAsync("SUPA", "Acme Supply Co");
        await parties.SetSupplierAddressAsync(supplier.Id, new Address { Street = "Factory Lane", Number = "7", PostalCode = "2000", City = "Antwerp" });
        await parties.AddSupplierModelMapAsync(supplier.Id, "FJORD");

        var orderId = await SeedOrderAsync(factory);
        var units = new ProductionUnitService(factory, OfficeUser);
        await units.SpawnForOrderAsync(orderId);

        var purchasing = new PurchasingService(factory, OfficeUser);
        var sweep = await purchasing.GenerateOrdersAsync();
        var supplierOrderId = Assert.Single(sweep.SupplierOrderIds);
        await purchasing.SendAsync(supplierOrderId);
        return supplierOrderId;
    }

    private static string NewPdfOutputRoot() => Path.Combine(Path.GetTempPath(), "po1-pdf-tests", Guid.NewGuid().ToString("N"));
    private static string NewXmlOutputRoot() => Path.Combine(Path.GetTempPath(), "po1-xml-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GeneratePdf_ContainsPoNumber_SupplierName_AndGroupedIdentityWithCount()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierOrderId = await SeedSentPurchaseOrderAsync(factory);
        var order = await new PurchasingService(factory, OfficeUser).GetOrderAsync(supplierOrderId);

        var pdf = new SupplierOrderPdf(factory, new PdfExportService(new PdfTemplateService()), NewPdfOutputRoot());
        var filePath = await pdf.GenerateAsync(supplierOrderId);

        Assert.True(new FileInfo(filePath).Length > 0);
        using var readerDoc = new PdfDocument(new PdfReader(filePath));
        var pageText = PdfTextExtractor.GetTextFromPage(readerDoc.GetFirstPage());

        Assert.Contains(order!.PoNumber, pageText);
        Assert.Contains("Acme Supply Co", pageText);
        Assert.Contains("FJORD/SOFA/V1 / BLU", pageText);
        Assert.Contains("2", pageText);
    }

    [Fact]
    public async Task GenerateXml_Parses_ContainsPoNumber_SupplierName_AndOneLinePerIdentityGroup()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierOrderId = await SeedSentPurchaseOrderAsync(factory);
        var order = await new PurchasingService(factory, OfficeUser).GetOrderAsync(supplierOrderId);

        var xml = new SupplierOrderXml(factory, NewXmlOutputRoot());
        var filePath = await xml.GenerateAsync(supplierOrderId);

        Assert.True(File.Exists(filePath));
        var document = XDocument.Load(filePath);
        Assert.NotNull(document.Root);
        var text = document.ToString();
        Assert.Contains(order!.PoNumber, text);
        Assert.Contains("Acme Supply Co", text);

        var lineElements = document.Descendants().Where(e => e.Name.LocalName == "OrderLine").ToList();
        Assert.Single(lineElements);
    }

    private static async Task SeedDefaultFirmAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.Firms.Add(new Firm
        {
            Code = "ALP",
            Name = "Alpine Living",
            VatNumber = "BE0999999999",
            Iban = "BE68539007547034",
            Bic = "MAPLBEBB",
            Address = new Address { Street = "Maple Row", Number = "12", PostalCode = "9990", City = "Fairbrook" },
            IsDefault = true,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PurchaseOrderXml_BuyerIsDefaultFirm()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedDefaultFirmAsync(factory);
        var supplierOrderId = await SeedSentPurchaseOrderAsync(factory);

        var xml = new SupplierOrderXml(factory, NewXmlOutputRoot());
        var filePath = await xml.GenerateAsync(supplierOrderId);

        var document = XDocument.Load(filePath);
        var text = document.ToString();
        Assert.Contains("Alpine Living", text);
        Assert.Contains("BE0999999999", text);
        Assert.DoesNotContain("CheapFurniturePlanner", text);
    }

    [Fact]
    public async Task PurchaseOrderPdf_ShowsFirmBlock()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedDefaultFirmAsync(factory);
        var supplierOrderId = await SeedSentPurchaseOrderAsync(factory);

        var pdf = new SupplierOrderPdf(factory, new PdfExportService(new PdfTemplateService()), NewPdfOutputRoot());
        var filePath = await pdf.GenerateAsync(supplierOrderId);

        using var readerDoc = new PdfDocument(new PdfReader(filePath));
        var pageText = PdfTextExtractor.GetTextFromPage(readerDoc.GetFirstPage());
        Assert.Contains("Alpine Living", pageText);
        Assert.Contains("BE0999999999", pageText);
    }
}
