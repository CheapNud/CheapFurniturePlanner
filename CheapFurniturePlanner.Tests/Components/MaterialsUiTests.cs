using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
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

// Task 6: /materials (Forecast/Stock/Orders tabs) and /materials/orders/{Id}. Harness mirrors
// PurchasingUiTests (bUnit + in-memory SQLite, real services) plus MaterialNeedsServiceTests'
// embedded Fjord catalogue seed for a real, resolvable forecast.
public class MaterialsUiTests : TestContext
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
    private static readonly Dictionary<string, string> StdSelections = new() { ["DEPTH"] = "STD", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" };

    // Same embedded "Fjord" demo bundle MaterialNeedsServiceTests round-trips.
    private static void SeedPublishedCatalogue(IDbContextFactory<FurniturePlannerContext> factory, string version)
    {
        var asm = typeof(CataloguePublishService).Assembly;
        using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json")
            ?? throw new InvalidOperationException("Embedded resource 'CheapFurniturePlanner.Seed.demo-catalogue.json' not found.");
        using var reader = new StreamReader(stream);
        var snapshot = CanonicalJson.Deserialize<CatalogueSnapshot>(reader.ReadToEnd())
            ?? throw new InvalidOperationException("Failed to deserialize embedded demo-catalogue.json.");
        snapshot.Version = version;
        snapshot.ContentHash = snapshot.ComputeContentHash();
        var bundleJson = CanonicalJson.Serialize(snapshot);

        using var db = factory.CreateDbContext();
        db.PublishedCatalogues.Add(new PublishedCatalogue { Version = version, ContentHash = snapshot.ContentHash, BundleJson = bundleJson, IsCurrent = true });
        db.SaveChanges();
    }

    private static async Task<(int OrderId, int LineId)> SeedOrderLineAsync(IDbContextFactory<FurniturePlannerContext> factory,
        string modelCode, string elementCode, string pinnedVersion, DateTime? promisedDeliveryDate = null)
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
            PinnedCatalogueVersion = pinnedVersion,
            PromisedDeliveryDate = promisedDeliveryDate,
        };
        order.Lines.Add(new OrderLine
        {
            DisplayIndex = 0,
            Kind = OrderLineKind.ConfiguredElement,
            ModelCode = modelCode,
            ElementCode = elementCode,
            SelectionsJson = CanonicalJson.Serialize(StdSelections),
            FabricColorCode = "AQUA-BLUE",
            DeliverToWarehouse = true,
            Quantity = 1,
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (order.Id, order.Lines[0].Id);
    }

    private static async Task<int> SeedUnitAsync(IDbContextFactory<FurniturePlannerContext> factory, int orderId, int lineId, int sequenceNumber)
    {
        await using var db = await factory.CreateDbContextAsync();
        var unit = new ProductionUnit
        {
            OrderId = orderId,
            OrderLineId = lineId,
            SequenceNumber = sequenceNumber,
            UnitCode = $"ORD-{orderId}-{sequenceNumber}",
            State = ProductionUnitState.Expected,
            CreatedAt = DateTime.UtcNow,
        };
        db.ProductionUnits.Add(unit);
        await db.SaveChangesAsync();
        return unit.Id;
    }

    // One in-house Fjord unit (resolvable) plus one unmapped "GHOST" unit (unresolved) - the
    // minimal seed the Forecast tab needs to show real rows and the warning together.
    private static async Task SeedForecastableAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        SeedPublishedCatalogue(factory, "1");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = "FJORD" }); // in-house marker
            await db.SaveChangesAsync();
        }
        var (inHouseOrderId, inHouseLineId) = await SeedOrderLineAsync(factory, "FJORD", "FJ2", "1");
        await SeedUnitAsync(factory, inHouseOrderId, inHouseLineId, 1);

        var (unresolvedOrderId, unresolvedLineId) = await SeedOrderLineAsync(factory, "GHOST", "FJ2", "1");
        await SeedUnitAsync(factory, unresolvedOrderId, unresolvedLineId, 1);
    }

    private static async Task<int> SeedSupplierAsync(IDbContextFactory<FurniturePlannerContext> factory, string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = code, Name = code };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    private IRenderedComponent<MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory,
        MaterialNeedsService materialNeeds, MaterialOrderService materialOrders, PartyService parties, MaterialPlanningService? materialPlanning = null)
    {
        var pdfRoot = Path.Combine(Path.GetTempPath(), "mat-mo-pdf-tests", Guid.NewGuid().ToString("N"));
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(OfficeUser);
        Services.AddSingleton(materialNeeds);
        Services.AddSingleton(materialOrders);
        Services.AddSingleton(parties);
        Services.AddSingleton(materialPlanning ?? new MaterialPlanningService(factory, OfficeUser));
        Services.AddSingleton(sp => new MaterialOrderPdf(factory, new PdfExportService(new PdfTemplateService()), pdfRoot));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var dialogProvider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        return dialogProvider;
    }

    [Fact]
    public async Task Forecast_Compute_RendersRowsWithEditableToOrder_AndUnresolvedWarning()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedForecastableAsync(factory);
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        ConfigureServices(factory, materialNeeds, materialOrders, parties);

        var cut = Render<MaterialsPage>();

        var computeButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Compute");
        await cut.InvokeAsync(() => computeButton.Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("FBX", cut.Markup); // Frame row's code
            Assert.Contains("GHOST", cut.Markup); // unresolved warning
        });

        // The Frame row's to-order field starts pinned to the computed SuggestedToOrder and is
        // genuinely editable, not read-only display. Rows sort by Kind (Foam=0, Frame=1, ...), so
        // Frame is always the second field regardless of its numeric quantity.
        var toOrderFields = cut.FindComponents<MudNumericField<decimal>>();
        var frameField = toOrderFields[1];
        await cut.InvokeAsync(() => frameField.Instance.ValueChanged.InvokeAsync(5m));
        Assert.Equal(5m, frameField.Instance.Value);
    }

    [Fact]
    public async Task Stock_NegativeAmount_RendersInErrorColor()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "FM-STD", Amount = -3m, UpdatedAt = DateTime.UtcNow });
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Cotton, Code = "COT-STD", Amount = 4m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        ConfigureServices(factory, materialNeeds, materialOrders, parties);

        var cut = Render<MaterialsPage>();
        var stockTab = cut.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Stock");
        await cut.InvokeAsync(() => stockTab.Click());

        cut.WaitForAssertion(() => Assert.Contains("FM-STD", cut.Markup));

        // Two amount cells only (the h4 page title is also a MudText, filtered out by Typo) -
        // StockAsync orders Foam(0) before Cotton(2), so index 0 is the negative row.
        var amountTexts = cut.FindComponents<MudText>().Where(t => t.Instance.Typo != Typo.h4).ToList();
        Assert.Equal(2, amountTexts.Count);
        Assert.Equal(Color.Error, amountTexts[0].Instance.Color);
        Assert.NotEqual(Color.Error, amountTexts[1].Instance.Color);
    }

    [Fact]
    public async Task Forecast_CreateOrder_UsesEditedQuantities_NotOriginalSuggestion()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedForecastableAsync(factory);
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        var dialogProvider = ConfigureServices(factory, materialNeeds, materialOrders, parties);

        var cut = Render<MaterialsPage>();
        var computeButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Compute");
        await cut.InvokeAsync(() => computeButton.Click());
        cut.WaitForAssertion(() => Assert.Contains("FBX", cut.Markup));

        // Edit the Frame row's to-order quantity to 7, then select just that row. Rows sort by
        // Kind (Foam=0, Frame=1, ...), so index 1 is always the Frame row's field/checkbox.
        var frameField = cut.FindComponents<MudNumericField<decimal>>()[1];
        await cut.InvokeAsync(() => frameField.Instance.ValueChanged.InvokeAsync(7m));
        var checkbox = cut.FindComponents<MudCheckBox<bool>>()[1];
        await cut.InvokeAsync(() => checkbox.Instance.ValueChanged.InvokeAsync(true));

        var createOrderButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Create order");
        var pendingClick = cut.InvokeAsync(() => createOrderButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<CheapFurniturePlanner.Components.Materials.CreateOrderDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<CheapFurniturePlanner.Components.Materials.CreateOrderDialog>();
        var supplierSelect = dialog.FindComponent<MudSelect<int?>>();
        await dialog.InvokeAsync(() => supplierSelect.Instance.ValueChanged.InvokeAsync(supplierId));
        var createButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Create");
        await dialog.InvokeAsync(() => createButton.Click());
        await pendingClick;

        var orders = await materialOrders.ListAsync();
        var order = Assert.Single(orders);
        var line = Assert.Single(order.Lines);
        Assert.Equal("FBX", line.Code);
        Assert.Equal(7m, line.QuantityOrdered); // the edited value, not the original SuggestedToOrder (1)
    }

    [Fact]
    public async Task Detail_ReceiveField_OnlyVisibleWhileSent()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var order = await materialOrders.CreateDraftAsync(supplierId, [new MaterialOrderLine { Kind = MaterialKind.Foam, Code = "F-100", QuantityOrdered = 10m }]);
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var parties = new PartyService(factory, OfficeUser);
        ConfigureServices(factory, materialNeeds, materialOrders, parties);

        var draftCut = Render<MaterialOrderPage>(p => p.Add(x => x.Id, order.Id));
        draftCut.WaitForAssertion(() => Assert.Contains("F-100", draftCut.Markup));
        Assert.DoesNotContain(draftCut.FindAll("button"), b => b.TextContent.Trim() == "Receive");

        await materialOrders.SendAsync(order.Id);
        var sentCut = Render<MaterialOrderPage>(p => p.Add(x => x.Id, order.Id));
        sentCut.WaitForAssertion(() => Assert.Contains(sentCut.FindAll("button"), b => b.TextContent.Trim() == "Receive"));
    }

    // Task 5 of SP-3: Stock tab union listing (stock rows + profiled/termed identities), below-min
    // highlight, and the Profile/Movements dialogs.

    [Fact]
    public async Task Stock_UnionListing_ShowsProfiledButStocklessMaterial()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Cotton, Code = "COT-STD", Amount = 4m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var materialPlanning = new MaterialPlanningService(factory, OfficeUser);
        await materialPlanning.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Foam, Code = "F-20", MinimumStock = 5m });
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        ConfigureServices(factory, materialNeeds, materialOrders, parties, materialPlanning);

        var cut = Render<MaterialsPage>();
        var stockTab = cut.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Stock");
        await cut.InvokeAsync(() => stockTab.Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("COT-STD", cut.Markup); // has a stock row
            Assert.Contains("F-20", cut.Markup); // profiled but never received - still listed
        });
    }

    [Fact]
    public async Task Stock_BelowMinimum_HighlightsOnlyWhenFlagged()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "F-LOW", Amount = 3m, UpdatedAt = DateTime.UtcNow });
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Frame, Code = "FR-OK", Amount = 10m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var materialPlanning = new MaterialPlanningService(factory, OfficeUser);
        await materialPlanning.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Foam, Code = "F-LOW", MinimumStock = 5m }); // 3 < 5
        await materialPlanning.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Frame, Code = "FR-OK", MinimumStock = 5m }); // 10 >= 5
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        ConfigureServices(factory, materialNeeds, materialOrders, parties, materialPlanning);

        var cut = Render<MaterialsPage>();
        var stockTab = cut.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Stock");
        await cut.InvokeAsync(() => stockTab.Click());
        cut.WaitForAssertion(() => Assert.Contains("F-LOW", cut.Markup));

        // Rows sort by Kind then Code ordinal: Foam(0) before Frame(1) - index 0 is the flagged row.
        var amountTexts = cut.FindComponents<MudText>().Where(t => t.Instance.Typo != Typo.h4).ToList();
        Assert.Equal(2, amountTexts.Count);
        Assert.Equal(Color.Error, amountTexts[0].Instance.Color);
        Assert.NotEqual(Color.Error, amountTexts[1].Instance.Color);
    }

    [Fact]
    public async Task Stock_ProfileDialog_RoundTripsMinimumStockAndOverride()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "F-20", Amount = 4m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var materialPlanning = new MaterialPlanningService(factory, OfficeUser);
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        var dialogProvider = ConfigureServices(factory, materialNeeds, materialOrders, parties, materialPlanning);

        var cut = Render<MaterialsPage>();
        var stockTab = cut.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Stock");
        await cut.InvokeAsync(() => stockTab.Click());
        cut.WaitForAssertion(() => Assert.Contains("F-20", cut.Markup));

        var profileButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Profile");
        var pendingClick = cut.InvokeAsync(() => profileButton.Click());
        dialogProvider.WaitForState(() => dialogProvider.FindComponents<CheapFurniturePlanner.Components.Materials.MaterialProfileDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<CheapFurniturePlanner.Components.Materials.MaterialProfileDialog>();

        // MinimumStock is the T="decimal" field - AverageUsageOverride is T="decimal?", a distinct type.
        dialog.WaitForState(() => dialog.FindComponents<MudNumericField<decimal>>().Count > 0);
        var minimumStockField = dialog.FindComponents<MudNumericField<decimal>>().Single();
        await dialog.InvokeAsync(() => minimumStockField.Instance.ValueChanged.InvokeAsync(9m));

        var saveButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Save");
        await dialog.InvokeAsync(() => saveButton.Click());
        await pendingClick;

        var profiles = await materialPlanning.ProfilesAsync();
        var profile = Assert.Single(profiles);
        Assert.Equal("F-20", profile.Code);
        Assert.Equal(9m, profile.MinimumStock);
    }

    [Fact]
    public async Task Stock_ProfileDialog_PreferredToggle_SwapsPreferredTerm()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierAId = await SeedSupplierAsync(factory, "SUPA");
        var supplierBId = await SeedSupplierAsync(factory, "SUPB");
        var materialPlanning = new MaterialPlanningService(factory, OfficeUser);
        var first = await materialPlanning.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 3 });
        var second = await materialPlanning.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierBId, DeliveryTimeDays = 5 });
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        var dialogProvider = ConfigureServices(factory, materialNeeds, materialOrders, parties, materialPlanning);

        var cut = Render<MaterialsPage>();
        var stockTab = cut.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Stock");
        await cut.InvokeAsync(() => stockTab.Click());
        cut.WaitForAssertion(() => Assert.Contains("F-20", cut.Markup)); // termed-but-stockless row, via union listing

        var profileButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Profile");
        var pendingDialogOpen = cut.InvokeAsync(() => profileButton.Click());
        dialogProvider.WaitForState(() => dialogProvider.FindComponents<CheapFurniturePlanner.Components.Materials.MaterialProfileDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<CheapFurniturePlanner.Components.Materials.MaterialProfileDialog>();

        dialog.WaitForState(() => dialog.FindAll("button").Any(b => b.TextContent.Trim() == "Make preferred"));
        var makePreferredButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Make preferred");
        var pendingClick = dialog.InvokeAsync(() => makePreferredButton.Click());

        dialogProvider.WaitForState(() => dialogProvider.FindComponents<MudMessageBox>().Count > 0);
        var messageBox = dialogProvider.FindComponent<MudMessageBox>();
        var confirmButton = messageBox.FindAll("button").Single(b => b.TextContent.Trim() == "Make preferred");
        await dialog.InvokeAsync(() => confirmButton.Click());
        await pendingClick;

        var terms = await materialPlanning.TermsAsync(MaterialKind.Foam, "F-20", null);
        Assert.True(terms.Single(t => t.Id == second.Id).IsPreferred);
        Assert.False(terms.Single(t => t.Id == first.Id).IsPreferred);

        var cancelButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel");
        await dialog.InvokeAsync(() => cancelButton.Click());
        await pendingDialogOpen;
    }

    [Fact]
    public async Task Stock_MovementsDialog_RendersTypedRows()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "F-20", Amount = 4m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        await materialNeeds.AdjustStockAsync(MaterialKind.Foam, "F-20", null, 9m); // writes a +5 Adjustment movement
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        var materialPlanning = new MaterialPlanningService(factory, OfficeUser);
        var dialogProvider = ConfigureServices(factory, materialNeeds, materialOrders, parties, materialPlanning);

        var cut = Render<MaterialsPage>();
        var stockTab = cut.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Stock");
        await cut.InvokeAsync(() => stockTab.Click());
        cut.WaitForAssertion(() => Assert.Contains("F-20", cut.Markup));

        var movementsButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Movements");
        await cut.InvokeAsync(() => movementsButton.Click());
        dialogProvider.WaitForState(() => dialogProvider.FindComponents<CheapFurniturePlanner.Components.Materials.MaterialMovementsDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<CheapFurniturePlanner.Components.Materials.MaterialMovementsDialog>();

        dialog.WaitForAssertion(() =>
        {
            Assert.Contains("Adjustment", dialog.Markup);
            Assert.Contains("+5", dialog.Markup);
        });
    }

    // Task 6 of SP-3: forecast tab planning columns + grouped-create flow, priced order detail.

    [Fact]
    public async Task Forecast_CreateOrder_GroupsByPreferredSupplier_AndFallsBackForUnassignedRemainder()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedForecastableAsync(factory);
        var supplierAId = await SeedSupplierAsync(factory, "SUPA");
        var supplierBId = await SeedSupplierAsync(factory, "SUPB");
        var supplierCId = await SeedSupplierAsync(factory, "SUPC");
        await using (var db = await factory.CreateDbContextAsync())
        {
            // Frame/Foam each get a preferred supplier; Cotton is left termless - it must fall
            // back to the manual supplier-pick dialog instead of being silently dropped.
            db.MaterialSupplierTerms.Add(new MaterialSupplierTerm { Kind = MaterialKind.Frame, Code = "FBX", SupplierId = supplierAId, DeliveryTimeDays = 3, IsPreferred = true });
            db.MaterialSupplierTerms.Add(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "FM-STD", SupplierId = supplierBId, DeliveryTimeDays = 3, IsPreferred = true });
            await db.SaveChangesAsync();
        }
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        var dialogProvider = ConfigureServices(factory, materialNeeds, materialOrders, parties);

        var cut = Render<MaterialsPage>();
        var computeButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Compute");
        await cut.InvokeAsync(() => computeButton.Click());
        cut.WaitForAssertion(() => Assert.Contains("FBX", cut.Markup));

        // Rows sort Foam(0), Frame(1), Cotton(2), Fabric(3), Misc(4) - select the two termed rows
        // plus the termless Cotton row.
        var checkboxes = cut.FindComponents<MudCheckBox<bool>>();
        await cut.InvokeAsync(() => checkboxes[0].Instance.ValueChanged.InvokeAsync(true)); // Foam
        await cut.InvokeAsync(() => checkboxes[1].Instance.ValueChanged.InvokeAsync(true)); // Frame
        await cut.InvokeAsync(() => checkboxes[2].Instance.ValueChanged.InvokeAsync(true)); // Cotton

        var createOrderButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Create order");
        var pendingClick = cut.InvokeAsync(() => createOrderButton.Click());

        // The grouped drafts (Frame->A, Foam->B) land immediately; only the Cotton remainder opens
        // the fallback dialog.
        dialogProvider.WaitForState(() => dialogProvider.FindComponents<CheapFurniturePlanner.Components.Materials.CreateOrderDialog>().Count > 0);
        var dialog = dialogProvider.FindComponent<CheapFurniturePlanner.Components.Materials.CreateOrderDialog>();
        var fallbackLine = Assert.Single(dialog.Instance.Lines);
        Assert.Equal("COT-STD", fallbackLine.Code);

        var supplierSelect = dialog.FindComponent<MudSelect<int?>>();
        await dialog.InvokeAsync(() => supplierSelect.Instance.ValueChanged.InvokeAsync(supplierCId));
        var createButton = dialog.FindAll("button").Single(b => b.TextContent.Trim() == "Create");
        await dialog.InvokeAsync(() => createButton.Click());
        await pendingClick;

        var orders = await materialOrders.ListAsync();
        Assert.Equal(3, orders.Count);
        Assert.Contains(orders, o => o.SupplierId == supplierAId && o.Lines.Any(l => l.Code == "FBX"));
        Assert.Contains(orders, o => o.SupplierId == supplierBId && o.Lines.Any(l => l.Code == "FM-STD"));
        Assert.Contains(orders, o => o.SupplierId == supplierCId && o.Lines.Any(l => l.Code == "COT-STD"));
    }

    [Fact]
    public async Task Forecast_OrderByOverdue_RendersRed()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = "FJORD" }); // in-house marker
            await db.SaveChangesAsync();
        }
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        await using (var db = await factory.CreateDbContextAsync())
        {
            // 30-day lead time against a promise only 5 days out - the derived order-by date lands
            // well in the past, so OrderByOverdue is true regardless of exactly when the test runs.
            db.MaterialSupplierTerms.Add(new MaterialSupplierTerm { Kind = MaterialKind.Frame, Code = "FBX", SupplierId = supplierId, DeliveryTimeDays = 30, IsPreferred = true });
            await db.SaveChangesAsync();
        }
        var (orderId, lineId) = await SeedOrderLineAsync(factory, "FJORD", "FJ2", "1", promisedDeliveryDate: DateTime.UtcNow.AddDays(5));
        await SeedUnitAsync(factory, orderId, lineId, 1);

        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        ConfigureServices(factory, materialNeeds, materialOrders, parties);

        var cut = Render<MaterialsPage>();
        var computeButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Compute");
        await cut.InvokeAsync(() => computeButton.Click());
        cut.WaitForAssertion(() => Assert.Contains("FBX", cut.Markup));

        // Read the same forecast independently (near-simultaneous clock reads) to know the exact
        // rendered date text, then find that cell and check its color.
        var expectedRow = (await materialNeeds.ComputeAsync()).Rows.Single(r => r.Code == "FBX");
        Assert.True(expectedRow.OrderByOverdue);
        var expectedDateText = expectedRow.OrderByDate!.Value.ToString("yyyy-MM-dd");

        var orderByCell = cut.FindComponents<MudText>().Single(t => t.Markup.Contains(expectedDateText));
        Assert.Equal(Color.Error, orderByCell.Instance.Color);
    }

    [Fact]
    public async Task Forecast_EstimatedCost_TracksEditedToOrderQuantity()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedForecastableAsync(factory);
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialSupplierTerms.Add(new MaterialSupplierTerm { Kind = MaterialKind.Frame, Code = "FBX", SupplierId = supplierId, DeliveryTimeDays = 3, UnitPrice = 12.5m, IsPreferred = true });
            await db.SaveChangesAsync();
        }
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var parties = new PartyService(factory, OfficeUser);
        ConfigureServices(factory, materialNeeds, materialOrders, parties);

        var cut = Render<MaterialsPage>();
        var computeButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Compute");
        await cut.InvokeAsync(() => computeButton.Click());
        cut.WaitForAssertion(() => Assert.Contains("FBX", cut.Markup));

        // Frame is index 1 (Foam=0, Frame=1, ...) - edit its to-order quantity away from the
        // suggestion and confirm the estimated-cost cell recomputes against the EDITED quantity.
        var frameField = cut.FindComponents<MudNumericField<decimal>>()[1];
        await cut.InvokeAsync(() => frameField.Instance.ValueChanged.InvokeAsync(7m));

        Assert.Contains((7m * 12.5m).ToString("C2"), cut.Markup);
    }

    [Fact]
    public async Task Detail_PriceColumn_ShowsPricesAndTotal_WhenAllLinesPriced()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materialOrders = new MaterialOrderService(factory, OfficeUser);
        var order = await materialOrders.CreateDraftAsync(supplierId,
        [
            new MaterialOrderLine { Kind = MaterialKind.Foam, Code = "F-100", QuantityOrdered = 10m, UnitPrice = 2.5m },
            new MaterialOrderLine { Kind = MaterialKind.Frame, Code = "FR-100", QuantityOrdered = 4m, UnitPrice = 5m },
        ]);
        var materialNeeds = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var parties = new PartyService(factory, OfficeUser);
        ConfigureServices(factory, materialNeeds, materialOrders, parties);

        var cut = Render<MaterialOrderPage>(p => p.Add(x => x.Id, order.Id));
        cut.WaitForAssertion(() => Assert.Contains("F-100", cut.Markup));

        Assert.Contains(2.5m.ToString("C2"), cut.Markup);
        Assert.Contains(5m.ToString("C2"), cut.Markup);
        Assert.Contains((10m * 2.5m + 4m * 5m).ToString("C2"), cut.Markup); // total - both lines priced
    }
}
