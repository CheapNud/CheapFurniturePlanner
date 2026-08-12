using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using CheapHelpers.Services.DataExchange.Pdf;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 4: material purchase order PDF. Harness mirrors SupplierOrderDocumentTests: real
// PdfExportService, temp output root, iText text extraction.
public class MaterialOrderPdfTests
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
    private static string NewPdfOutputRoot() => Path.Combine(Path.GetTempPath(), "mpo-pdf-tests", Guid.NewGuid().ToString("N"));

    private static async Task<int> SeedSupplierAsync(IDbContextFactory<FurniturePlannerContext> factory, string code, string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = code, Name = name };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
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
    public async Task GeneratePdf_ContainsNumber_SupplierName_FirmBlock_AndLineCode()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedDefaultFirmAsync(factory);
        var supplierId = await SeedSupplierAsync(factory, "WOODWORKS", "Woodworks Fine");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId,
            [new MaterialOrderLine { Kind = MaterialKind.Foam, Code = "F-100", HardnessCode = "H35", DisplayName = "Foam H35", QuantityOrdered = 20m }]);
        await materials.SendAsync(order.Id);

        var pdf = new MaterialOrderPdf(factory, new PdfExportService(new PdfTemplateService()), NewPdfOutputRoot());
        var filePath = await pdf.GenerateAsync(order.Id);

        Assert.True(new FileInfo(filePath).Length > 0);
        using var readerDoc = new PdfDocument(new PdfReader(filePath));
        var pageText = PdfTextExtractor.GetTextFromPage(readerDoc.GetFirstPage());

        Assert.Contains(order.Number, pageText);
        Assert.Contains("Woodworks Fine", pageText);
        Assert.Contains("Alpine Living", pageText);
        Assert.Contains("BE0999999999", pageText);
        Assert.Contains("F-100", pageText);
    }

    [Fact]
    public async Task GeneratePdf_NoDefaultFirm_OmitsFirmBlock_StillRenders()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "WOODWORKS", "Woodworks Fine");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId,
            [new MaterialOrderLine { Kind = MaterialKind.Cotton, Code = "COT-1", QuantityOrdered = 5m }]);

        var pdf = new MaterialOrderPdf(factory, new PdfExportService(new PdfTemplateService()), NewPdfOutputRoot());
        var filePath = await pdf.GenerateAsync(order.Id);

        using var readerDoc = new PdfDocument(new PdfReader(filePath));
        var pageText = PdfTextExtractor.GetTextFromPage(readerDoc.GetFirstPage());
        Assert.Contains(order.Number, pageText);
        Assert.Contains("Woodworks Fine", pageText);
        Assert.DoesNotContain("Ordered by", pageText);
    }
}
