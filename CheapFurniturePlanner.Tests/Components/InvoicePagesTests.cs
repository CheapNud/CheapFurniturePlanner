using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using CheapHelpers.Services.DataExchange.Pdf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 5: /invoices lists invoices and edits MarketVatRate rows; /invoices/{Id} shows a single
// invoice, marks it paid, and raises credit notes. Harness mirrors ReceivingPageTests (bUnit +
// in-memory SQLite, real InvoicingService) plus ServiceTicketPageTests' real InvoicePdf wiring
// (InvoicePage injects it even though these tests never click the PDF button).
public class InvoicePagesTests : TestContext
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

    // Seeds a Seller/Consumer/placed Order with one line, a BE 21% VAT rate, and issues the
    // invoice through the real service - mirrors InvoicingServiceTests.SeedPlacedOrderAsync.
    private static async Task<Invoice> SeedInvoiceAsync(IDbContextFactory<FurniturePlannerContext> factory, InvoicingService invoicing)
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop" };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var order = new Order
        {
            OrderNumber = $"ORD-2026-{await db.Orders.CountAsync() + 1:D4}",
            SellerId = seller.Id,
            ConsumerId = consumer.Id,
            MarketCode = "BE",
            State = OrderState.Placed,
            Lines = [new OrderLine { DisplayIndex = 0, Kind = OrderLineKind.ConfiguredElement, Quantity = 1, UnitPrice = 100m, LineTotal = 100m, VariantCode = "K7E:V1" }],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        db.MarketVatRates.Add(new MarketVatRate { MarketCode = "BE", RatePercent = 21m });
        await db.SaveChangesAsync();

        return await invoicing.CreateInvoiceAsync(order.Id);
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, InvoicingService invoicing)
    {
        var pdfRoot = Path.Combine(Path.GetTempPath(), "in1-invoice-page-tests", Guid.NewGuid().ToString("N"));
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(invoicing);
        Services.AddSingleton(sp => new InvoicePdf(factory, new PdfExportService(new PdfTemplateService()), pdfRoot));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task List_ShowsInvoice_AndVatEditorRoundTrips()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var invoicing = new InvoicingService(factory, office);
        var invoice = await SeedInvoiceAsync(factory, invoicing);
        ConfigureServices(factory, invoicing);

        var cut = Render<InvoicesPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(invoice.InvoiceNumber, cut.Markup);
            Assert.Contains("Open", cut.Markup);
            Assert.Contains("BE", cut.Markup);
            Assert.Contains("21", cut.Markup);
        });

        var marketField = cut.FindComponents<MudBlazor.MudTextField<string>>().Single(f => f.Instance.Label == "Market");
        await cut.InvokeAsync(() => marketField.Instance.ValueChanged.InvokeAsync("NL"));
        var rateField = cut.FindComponents<MudBlazor.MudNumericField<decimal>>().Single(f => f.Instance.Label == "Rate %");
        await cut.InvokeAsync(() => rateField.Instance.ValueChanged.InvokeAsync(19m));
        cut.FindAll("button").First(b => b.TextContent.Contains("Save")).Click();

        await cut.WaitForAssertionAsync(async () =>
        {
            var rates = await invoicing.VatRatesAsync();
            Assert.Contains(rates, r => r.MarketCode == "NL" && r.RatePercent == 19m);
        });
    }

    [Fact]
    public async Task Detail_MarkPaid_And_FullCredit()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var invoicing = new InvoicingService(factory, office);
        var invoice = await SeedInvoiceAsync(factory, invoicing);
        ConfigureServices(factory, invoicing);

        var cut = Render<InvoicePage>(p => p.Add(x => x.Id, invoice.Id));

        cut.WaitForAssertion(() => Assert.Contains("Mark paid", cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Contains("Mark paid")).Click();

        await cut.WaitForAssertionAsync(async () =>
        {
            var reloaded = await invoicing.GetInvoiceAsync(invoice.Id);
            Assert.True(reloaded!.IsPaid);
        });
        cut.WaitForAssertion(() => Assert.Contains("Paid", cut.Markup));

        cut.FindAll("button").First(b => b.TextContent.Contains("New credit note")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Full remaining balance", cut.Markup));

        var createButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Create");
        createButton.Click();

        await cut.WaitForAssertionAsync(async () =>
        {
            var reloaded = await invoicing.GetInvoiceAsync(invoice.Id);
            var creditNote = Assert.Single(reloaded!.CreditNotes);
            Assert.Equal(reloaded.GrossTotal, creditNote.GrossAmount);
        });
    }
}
