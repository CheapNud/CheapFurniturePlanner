using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Components.Purchasing;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using CheapHelpers.Services.DataExchange.Pdf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 5: /purchasing sweeps units into supplier POs and shows the unresolved-identity warning;
// /purchasing/{Id} sends a draft (through a confirm dialog), releases a Draft group's units back
// to the pool, and creates/attaches supplier delivery announcements. Harness mirrors
// InvoicePagesTests (bUnit + in-memory SQLite, real PurchasingService) plus
// SupplierOrderDocumentTests' real SupplierOrderPdf/SupplierOrderXml wiring (both pages inject
// them even though these tests never click the PDF/XML buttons).
public class PurchasingUiTests : TestContext
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

    private static readonly FakeCurrentUser OfficeUser = new("office-1", Roles.Office);

    private static async Task<int> SeedSupplierAsync(IDbContextFactory<FurniturePlannerContext> factory, string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = code, Name = code };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    // Seeds a Seller/Consumer/placed Order with one DeliverToWarehouse configured line and one
    // ProductionUnit for it - mirrors PurchasingServiceTests.SeedUnitAsync (the minimal chain a
    // sweep candidate needs).
    private static async Task<int> SeedUnitAsync(IDbContextFactory<FurniturePlannerContext> factory, string modelCode, int? lineSupplierId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop", Multiplier = 1m };
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
        };
        order.Lines.Add(new OrderLine
        {
            DisplayIndex = 0,
            Kind = OrderLineKind.ConfiguredElement,
            ModelCode = modelCode,
            SupplierId = lineSupplierId,
            DeliverToWarehouse = true,
            Quantity = 1,
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var line = order.Lines[0];
        var unit = new ProductionUnit
        {
            OrderId = order.Id,
            OrderLineId = line.Id,
            SequenceNumber = 1,
            UnitCode = $"{order.OrderNumber}-1-1",
            State = ProductionUnitState.Expected,
            CreatedAt = DateTime.UtcNow,
        };
        db.ProductionUnits.Add(unit);
        await db.SaveChangesAsync();
        return unit.Id;
    }

    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, PurchasingService purchasing)
    {
        var pdfRoot = Path.Combine(Path.GetTempPath(), "pu1-po-pdf-tests", Guid.NewGuid().ToString("N"));
        var xmlRoot = Path.Combine(Path.GetTempPath(), "pu1-po-xml-tests", Guid.NewGuid().ToString("N"));
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(OfficeUser);
        Services.AddSingleton(purchasing);
        Services.AddSingleton(sp => new PartyService(factory, OfficeUser));
        Services.AddSingleton(sp => new SupplierOrderPdf(factory, new PdfExportService(new PdfTemplateService()), pdfRoot));
        Services.AddSingleton(sp => new SupplierOrderXml(factory, xmlRoot));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var dialogProvider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        return dialogProvider;
    }

    [Fact]
    public async Task List_SweepCreatesOrder_AndUnresolvedWarningRenders()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var purchasing = new PurchasingService(factory, OfficeUser);
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        await SeedUnitAsync(factory, "GHOST"); // no line supplier, no model map - stays unresolved
        ConfigureServices(factory, purchasing);

        var cut = Render<PurchasingPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("GHOST", cut.Markup);
            Assert.Contains("No purchase orders yet", cut.Markup);
        });

        var sweepButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Generate orders");
        await cut.InvokeAsync(() => sweepButton.Click());

        var orders = await purchasing.ListOrdersAsync();
        var order = Assert.Single(orders);
        cut.WaitForAssertion(() => Assert.Contains(order.PoNumber, cut.Markup));
    }

    [Fact]
    public async Task Detail_Send_ThroughConfirm_FlipsStateChip()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var purchasing = new PurchasingService(factory, OfficeUser);
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var sweep = await purchasing.GenerateOrdersAsync();
        var orderId = Assert.Single(sweep.SupplierOrderIds);
        var dialogProvider = ConfigureServices(factory, purchasing);

        var cut = Render<PurchaseOrderPage>(p => p.Add(x => x.Id, orderId));

        cut.WaitForAssertion(() => Assert.Contains("Send", cut.Markup));
        var sendButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Send");
        var pendingClick = cut.InvokeAsync(() => sendButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        var confirmButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Send");
        await cut.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        cut.WaitForAssertion(() => Assert.Contains("Sent", cut.Markup));
        var reloaded = await purchasing.GetOrderAsync(orderId);
        Assert.Equal(SupplierOrderState.Sent, reloaded!.State);
    }

    [Fact]
    public async Task Detail_ReleaseGroup_OnDraft_ClearsUnitsFromOrder()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var purchasing = new PurchasingService(factory, OfficeUser);
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var sweep = await purchasing.GenerateOrdersAsync();
        var orderId = Assert.Single(sweep.SupplierOrderIds);
        ConfigureServices(factory, purchasing);

        var cut = Render<PurchaseOrderPage>(p => p.Add(x => x.Id, orderId));

        cut.WaitForAssertion(() => Assert.Contains("Release", cut.Markup));
        var releaseButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Release");
        await cut.InvokeAsync(() => releaseButton.Click());

        cut.WaitForAssertion(() => Assert.Contains("No units on this order", cut.Markup));
        var reloaded = await purchasing.GetOrderAsync(orderId);
        Assert.Empty(reloaded!.Units);
    }

    [Fact]
    public async Task Detail_CreateAnnouncement_ThenAttachUnit_ServiceReflectsLink()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var purchasing = new PurchasingService(factory, OfficeUser);
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var unitId = await SeedUnitAsync(factory, "MODELA", lineSupplierId: supplierId);
        var sweep = await purchasing.GenerateOrdersAsync();
        var orderId = Assert.Single(sweep.SupplierOrderIds);
        await purchasing.SendAsync(orderId);
        var dialogProvider = ConfigureServices(factory, purchasing);

        var cut = Render<PurchaseOrderPage>(p => p.Add(x => x.Id, orderId));

        cut.WaitForAssertion(() => Assert.Contains("New announcement", cut.Markup));
        var newAnnouncementButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "New announcement");
        var pendingClick = cut.InvokeAsync(() => newAnnouncementButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<AnnouncementDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<AnnouncementDialog>();
        var referenceField = dialog.FindComponent<MudTextField<string>>();
        await dialog.InvokeAsync(() => referenceField.Instance.ValueChanged.InvokeAsync("DN-0001"));
        var createButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Create");
        await cut.InvokeAsync(() => createButton.Click());
        await pendingClick;

        cut.WaitForAssertion(() => Assert.Contains("DN-0001", cut.Markup));

        var announcement = Assert.Single(await purchasing.ListAnnouncementsAsync(supplierId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<MudSelect<int?>>()));
        var unitSelect = cut.FindComponents<MudSelect<int?>>()[0];
        var announcementSelect = cut.FindComponents<MudSelect<int?>>()[1];
        await cut.InvokeAsync(() => unitSelect.Instance.ValueChanged.InvokeAsync(unitId));
        await cut.InvokeAsync(() => announcementSelect.Instance.ValueChanged.InvokeAsync(announcement.Id));

        var attachButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Attach");
        await cut.InvokeAsync(() => attachButton.Click());

        await cut.WaitForAssertionAsync(async () =>
        {
            var reloadedOrder = await purchasing.GetOrderAsync(orderId);
            Assert.Equal(announcement.Id, Assert.Single(reloadedOrder!.Units).SupplierDeliveryId);
        });
    }

    // Task 7: the unresolved panel's per-code "Mark in-house" button moves the code to the
    // in-house list (via PartyService.MarkModelInHouseAsync) and drops it from the unresolved
    // warning, mirroring PurchasingServiceTests.MarkInHouse_ExcludesFromSweepAndUnresolved at the
    // UI layer.
    [Fact]
    public async Task Unresolved_MarkInHouse_MovesCodeToInHouseList_AndDropsFromUnresolved()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var purchasing = new PurchasingService(factory, OfficeUser);
        await SeedUnitAsync(factory, "GHOST"); // no line supplier, no model map - unresolved
        ConfigureServices(factory, purchasing);

        var cut = Render<PurchasingPage>();
        cut.WaitForAssertion(() => Assert.Contains("GHOST", cut.Markup));

        var markButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Mark in-house");
        await cut.InvokeAsync(() => markButton.Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("In-house models", cut.Markup);
            Assert.DoesNotContain("Unresolved:", cut.Markup);
        });
        Assert.Empty(await purchasing.UnresolvedModelCodesAsync());
    }
}
