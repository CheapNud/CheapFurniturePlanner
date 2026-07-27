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

// Task 3: the "Export XML" buttons on InvoicePage/InvoicesPage. Harness mirrors InvoicePagesTests
// (bUnit + in-memory SQLite, real InvoicingService + InvoicePdf) plus a real UblExport wired to a
// temp output root, mirroring UblExportTests.
public class UblExportUiTests : TestContext
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
    // invoice through the real service - mirrors InvoicePagesTests.SeedInvoiceAsync.
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

    private string ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, InvoicingService invoicing, ICurrentUser currentUser)
    {
        var pdfRoot = Path.Combine(Path.GetTempPath(), "in1-invoice-page-tests", Guid.NewGuid().ToString("N"));
        var exportRoot = Path.Combine(Path.GetTempPath(), "ax1-ubl-ui-tests", Guid.NewGuid().ToString("N"));
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(invoicing);
        Services.AddSingleton(sp => new InvoicePdf(factory, new PdfExportService(new PdfTemplateService()), pdfRoot));
        Services.AddSingleton(sp => new UblExport(factory, currentUser, exportRoot));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
        return exportRoot;
    }

    [Fact]
    public async Task Detail_ExportXml_WritesFile_AndReloadsStamp()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var invoicing = new InvoicingService(factory, office);
        var invoice = await SeedInvoiceAsync(factory, invoicing);
        var exportRoot = ConfigureServices(factory, invoicing, office);

        var cut = Render<InvoicePage>(p => p.Add(x => x.Id, invoice.Id));

        cut.WaitForAssertion(() => Assert.Contains("Export XML", cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Contains("Export XML")).Click();

        await cut.WaitForAssertionAsync(async () =>
        {
            var reloaded = await invoicing.GetInvoiceAsync(invoice.Id);
            Assert.NotNull(reloaded!.ExportedAt);
            Assert.True(File.Exists(Path.Combine(exportRoot, $"{invoice.InvoiceNumber}.xml")));
        });
    }

    [Fact]
    public async Task List_ExportNew_ShowsExportedChip()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var invoicing = new InvoicingService(factory, office);
        await SeedInvoiceAsync(factory, invoicing);
        ConfigureServices(factory, invoicing, office);

        var cut = Render<InvoicesPage>();

        cut.WaitForAssertion(() => Assert.Contains("Export new", cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Contains("Export new")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Exported", cut.Markup));
    }
}
