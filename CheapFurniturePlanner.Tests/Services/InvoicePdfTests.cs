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

// SQLite harness mirrors SupplierReportFlowTests: in-memory SQLite, migrated schema, real
// InvoicingService for building fixtures so invoice/credit-note math is never hand-duplicated here.
public class InvoicePdfTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static readonly FakeCurrentUser OfficeUser = new("office-1", Roles.Office);

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

    private static async Task<int> SeedPlacedOrderAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop" };
        var consumer = new Consumer { Name = "Jansen", VatNumber = "BE0123456789" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var order = new Order { OrderNumber = "ORD-2026-0001", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = "BE", State = OrderState.Placed };
        order.Lines.Add(new OrderLine { Kind = OrderLineKind.ConfiguredElement, DisplayIndex = 0, Quantity = 1, UnitPrice = 1234.50m, LineTotal = 1234.50m, VariantCode = "K7E:V1" });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static string NewOutputRoot() => Path.Combine(Path.GetTempPath(), "in1-pdf-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InvoicePdf_ContainsNumbers_VatNumber_AndInvariantTotals()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MarketVatRates.Add(new MarketVatRate { MarketCode = "BE", RatePercent = 21m });
            await db.SaveChangesAsync();
        }
        var orderId = await SeedPlacedOrderAsync(factory);
        var invoicing = new InvoicingService(factory, OfficeUser);
        var invoice = await invoicing.CreateInvoiceAsync(orderId);

        var pdf = new InvoicePdf(factory, new PdfExportService(new PdfTemplateService()), NewOutputRoot());
        var filePath = await pdf.GenerateInvoiceAsync(invoice.Id);

        Assert.True(new FileInfo(filePath).Length > 0);
        using var readerDoc = new PdfDocument(new PdfReader(filePath));
        var pageText = PdfTextExtractor.GetTextFromPage(readerDoc.GetFirstPage());
        Assert.Contains(invoice.InvoiceNumber, pageText);
        Assert.Contains("ORD-2026-0001", pageText);
        Assert.Contains("BE0123456789", pageText);
        Assert.Contains("1234.50", pageText);
        Assert.DoesNotContain("1234,50", pageText);
        Assert.Contains(invoice.GrossTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), pageText);
    }

    [Fact]
    public async Task CreditNotePdf_ContainsNumberReasonAndAmount()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MarketVatRates.Add(new MarketVatRate { MarketCode = "BE", RatePercent = 21m });
            await db.SaveChangesAsync();
        }
        var orderId = await SeedPlacedOrderAsync(factory);
        var invoicing = new InvoicingService(factory, OfficeUser);
        var invoice = await invoicing.CreateInvoiceAsync(orderId);
        var creditNote = await invoicing.CreateCreditNoteAsync(invoice.Id, CreditReason.Goodwill);

        var pdf = new InvoicePdf(factory, new PdfExportService(new PdfTemplateService()), NewOutputRoot());
        var filePath = await pdf.GenerateCreditNoteAsync(creditNote.Id);

        Assert.True(new FileInfo(filePath).Length > 0);
        using var readerDoc = new PdfDocument(new PdfReader(filePath));
        var pageText = PdfTextExtractor.GetTextFromPage(readerDoc.GetFirstPage());
        Assert.Contains(creditNote.CreditNoteNumber, pageText);
        Assert.Contains("Goodwill", pageText);
        Assert.Contains(creditNote.GrossAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), pageText);
    }
}
