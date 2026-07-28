using System.Globalization;
using System.Xml.Linq;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors InvoicingServiceTests: in-memory SQLite, migrated schema, real
// InvoicingService for building fixtures so invoice/credit-note math is never hand-duplicated here.
public class UblExportTests
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

    private static async Task<int> SeedPlacedOrderAsync(IDbContextFactory<FurniturePlannerContext> factory,
        decimal orderDiscountPercent = 0m, string marketCode = "BE", params (decimal LineTotal, int Qty, decimal UnitPrice, decimal LineDiscount)[] lines)
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop" };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var order = new Order { OrderNumber = $"ORD-2026-{db.Orders.Count() + 1:D4}", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = marketCode, State = OrderState.Placed, OrderDiscountPercent = orderDiscountPercent };
        var displayIndex = 0;
        foreach (var (lineTotal, qty, unitPrice, lineDiscount) in lines)
        {
            order.Lines.Add(new OrderLine { Kind = OrderLineKind.ConfiguredElement, DisplayIndex = displayIndex++, Quantity = qty, UnitPrice = unitPrice, DiscountPercent = lineDiscount, LineTotal = lineTotal, VariantCode = $"K7E:V{displayIndex}" });
        }
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task SeedVatAsync(IDbContextFactory<FurniturePlannerContext> factory, string marketCode = "BE", decimal rate = 21m)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.MarketVatRates.Add(new MarketVatRate { MarketCode = marketCode, RatePercent = rate });
        await db.SaveChangesAsync();
    }

    private static async Task SetConsumerVatNumberAsync(IDbContextFactory<FurniturePlannerContext> factory, int orderId, string vatNumber)
    {
        await using var db = await factory.CreateDbContextAsync();
        var order = await db.Orders.FirstAsync(o => o.Id == orderId);
        var consumer = await db.Consumers.FirstAsync(c => c.Id == order.ConsumerId);
        consumer.VatNumber = vatNumber;
        await db.SaveChangesAsync();
    }

    private static string NewOutputRoot() => Path.Combine(Path.GetTempPath(), "ax1-ubl-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportInvoice_WritesParseableXml_WithSnapshotValues_AndStamps()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 10m, "BE", (90m, 1, 100m, 10m));
        await SetConsumerVatNumberAsync(factory, orderId, "BE0123456789");
        var invoicing = new InvoicingService(factory, OfficeUser);
        var invoice = await invoicing.CreateInvoiceAsync(orderId);

        var export = new UblExport(factory, OfficeUser, NewOutputRoot());
        var filePath = await export.ExportInvoiceAsync(invoice.Id);

        Assert.True(File.Exists(filePath));
        var document = XDocument.Load(filePath);
        Assert.NotNull(document.Root);
        var text = document.ToString();
        Assert.Contains(invoice.InvoiceNumber, text);
        Assert.Contains("Jansen", text);
        Assert.Contains("BE0123456789", text);

        var loaded = await invoicing.GetInvoiceAsync(invoice.Id);
        Assert.NotNull(loaded!.ExportedAt);
    }

    [Fact]
    public async Task UblExport_CarriesBuyerAddress()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var order = await db.Orders.Include(o => o.Consumer).FirstAsync(o => o.Id == orderId);
            order.Consumer!.PrimaryAddress = new Address { Street = "Church Road", Number = "12", PostalCode = "9000", City = "Oakwood" };
            await db.SaveChangesAsync();
        }
        var invoicing = new InvoicingService(factory, OfficeUser);
        var invoice = await invoicing.CreateInvoiceAsync(orderId);

        var export = new UblExport(factory, OfficeUser, NewOutputRoot());
        var filePath = await export.ExportInvoiceAsync(invoice.Id);

        var document = XDocument.Load(filePath);
        var customerParty = document.Descendants().First(e => e.Name.LocalName == "AccountingCustomerParty");
        var postalAddress = customerParty.Descendants().First(e => e.Name.LocalName == "PostalAddress");
        Assert.Contains("Church Road", postalAddress.ToString());
    }

    [Fact]
    public async Task ExportCreditNote_ReferencesInvoice_AndStamps()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var invoicing = new InvoicingService(factory, OfficeUser);
        var invoice = await invoicing.CreateInvoiceAsync(orderId);
        var creditNote = await invoicing.CreateCreditNoteAsync(invoice.Id, CreditReason.Goodwill);

        var export = new UblExport(factory, OfficeUser, NewOutputRoot());
        var filePath = await export.ExportCreditNoteAsync(creditNote.Id);

        Assert.True(File.Exists(filePath));
        var document = XDocument.Load(filePath);
        Assert.NotNull(document.Root);
        var text = document.ToString();
        Assert.Contains(creditNote.CreditNoteNumber, text);
        Assert.Contains(invoice.InvoiceNumber, text);
        Assert.Contains("Goodwill", text);

        await using var db = await factory.CreateDbContextAsync();
        var reloaded = await db.CreditNotes.AsNoTracking().FirstAsync(c => c.Id == creditNote.Id);
        Assert.NotNull(reloaded.ExportedAt);
    }

    [Fact]
    public async Task ExportNew_SkipsExported_OldestFirst()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var firstOrderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var secondOrderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (50m, 1, 50m, 0m));
        var invoicing = new InvoicingService(factory, OfficeUser);
        var firstInvoice = await invoicing.CreateInvoiceAsync(firstOrderId);
        var secondInvoice = await invoicing.CreateInvoiceAsync(secondOrderId);
        var creditNote = await invoicing.CreateCreditNoteAsync(firstInvoice.Id, CreditReason.Goodwill);

        var export = new UblExport(factory, OfficeUser, NewOutputRoot());
        await export.ExportInvoiceAsync(firstInvoice.Id);

        var writtenPaths = await export.ExportNewAsync();

        Assert.Equal(2, writtenPaths.Count);
        Assert.Contains($"{secondInvoice.InvoiceNumber}.xml", writtenPaths[0]);
        Assert.Contains($"{creditNote.CreditNoteNumber}.xml", writtenPaths[1]);

        var secondRun = await export.ExportNewAsync();
        Assert.Empty(secondRun);
    }

    [Fact]
    public async Task FailedWrite_LeavesStampNull()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var invoicing = new InvoicingService(factory, OfficeUser);
        var invoice = await invoicing.CreateInvoiceAsync(orderId);

        var brokenOutputRoot = Path.Combine(Path.GetTempPath(), "ax1-ubl-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(brokenOutputRoot)!);
        await File.WriteAllTextAsync(brokenOutputRoot, "not a directory");

        var export = new UblExport(factory, OfficeUser, brokenOutputRoot);
        await Assert.ThrowsAnyAsync<Exception>(() => export.ExportInvoiceAsync(invoice.Id));

        var loaded = await invoicing.GetInvoiceAsync(invoice.Id);
        Assert.Null(loaded!.ExportedAt);
    }

    [Fact]
    public async Task Guards_NonOfficeRejected_MissingDocThrows()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var invoicing = new InvoicingService(factory, OfficeUser);
        var invoice = await invoicing.CreateInvoiceAsync(orderId);

        var warehouseExport = new UblExport(factory, new FakeCurrentUser("wh-1", Roles.Warehouse), NewOutputRoot());
        await Assert.ThrowsAsync<InvalidOperationException>(() => warehouseExport.ExportInvoiceAsync(invoice.Id));

        var officeExport = new UblExport(factory, OfficeUser, NewOutputRoot());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => officeExport.ExportInvoiceAsync(999_999));
        Assert.Contains("not found", ex.Message);
    }

    // Three lines of 0.67 at 50% order discount: unrounded per-line net is 0.335, and naive
    // per-line rounding (0.34 x 3 = 1.02) disagrees with the header net, which rounds the summed
    // 1.005 once (1.01). The header and the lines must agree - see the reconciliation comment in
    // UblExport.MapInvoice.
    [Fact]
    public async Task ExportInvoice_LineTotalsSumToHeaderNet_WhenPerLineRoundingWouldDrift()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 0m);
        var orderId = await SeedPlacedOrderAsync(factory, 50m, "BE",
            (0.67m, 1, 0.67m, 0m), (0.67m, 1, 0.67m, 0m), (0.67m, 1, 0.67m, 0m));
        var invoicing = new InvoicingService(factory, OfficeUser);
        var invoice = await invoicing.CreateInvoiceAsync(orderId);
        Assert.Equal(1.01m, invoice.NetTotal);

        var export = new UblExport(factory, OfficeUser, NewOutputRoot());
        var filePath = await export.ExportInvoiceAsync(invoice.Id);

        var document = XDocument.Load(filePath);
        var headerNet = decimal.Parse(document.Descendants().First(e => e.Name.LocalName == "LegalMonetaryTotal")
            .Elements().First(e => e.Name.LocalName == "LineExtensionAmount").Value, CultureInfo.InvariantCulture);
        var lineNetSum = document.Descendants().Where(e => e.Name.LocalName == "InvoiceLine")
            .Select(line => decimal.Parse(line.Elements().First(e => e.Name.LocalName == "LineExtensionAmount").Value, CultureInfo.InvariantCulture))
            .Sum();

        Assert.Equal(1.01m, headerNet);
        Assert.Equal(1.01m, lineNetSum);
        Assert.Equal(headerNet, lineNetSum);
    }
}
