using System.Text.RegularExpressions;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 3: the in-house counterpart to PurchasingServiceTests' sweep - same three-state resolution
// rule (dropship-pinned / mapped-external / null-supplier in-house marker / unmapped), but summed
// into a material forecast instead of grouped into supplier POs. Harness mirrors CatalogueExportTests
// (embedded Fjord seed for a real, priceable snapshot) + PurchasingServiceTests (direct-EF seeding).
public class MaterialNeedsServiceTests
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

    private static string NewOutputRoot() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private static readonly FakeCurrentUser OfficeUser = new("office-1", Roles.Office);
    private static readonly Dictionary<string, string> StdSelections = new() { ["DEPTH"] = "STD", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" };

    // Same embedded "Fjord" demo bundle CatalogueExportTests round-trips - stamps the given version
    // + a real ComputeContentHash and inserts the row directly.
    private static string SeedPublishedCatalogue(IDbContextFactory<FurniturePlannerContext> factory, string version)
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
        return bundleJson;
    }

    // Seeds a Seller/Consumer/Placed order pinned to the given catalogue version, with one
    // configured line - the minimal chain a material-needs candidate needs.
    private static async Task<(int OrderId, int LineId)> SeedOrderLineAsync(IDbContextFactory<FurniturePlannerContext> factory,
        string modelCode, string elementCode, string? pinnedVersion, int? lineSupplierId = null, string fabricColorCode = "AQUA-BLUE")
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
        };
        order.Lines.Add(new OrderLine
        {
            DisplayIndex = 0,
            Kind = OrderLineKind.ConfiguredElement,
            ModelCode = modelCode,
            ElementCode = elementCode,
            SelectionsJson = CanonicalJson.Serialize(StdSelections),
            FabricColorCode = fabricColorCode,
            SupplierId = lineSupplierId,
            DeliverToWarehouse = true,
            Quantity = 1,
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (order.Id, order.Lines[0].Id);
    }

    private static async Task<int> SeedUnitAsync(IDbContextFactory<FurniturePlannerContext> factory,
        int orderId, int lineId, int sequenceNumber, ProductionUnitState state = ProductionUnitState.Expected)
    {
        await using var db = await factory.CreateDbContextAsync();
        var unit = new ProductionUnit
        {
            OrderId = orderId,
            OrderLineId = lineId,
            SequenceNumber = sequenceNumber,
            UnitCode = $"ORD-{orderId}-{sequenceNumber}",
            State = state,
            CreatedAt = DateTime.UtcNow,
        };
        db.ProductionUnits.Add(unit);
        await db.SaveChangesAsync();
        return unit.Id;
    }

    // FJ2's BOM under StdSelections (DEPTH=STD, MECH=NONE, HEAD unselected): frame FBX x1, foam
    // FM-STD x2 (the DEPTH=DEEP conditional pad line doesn't apply), cotton COT-STD x3.0, fabric
    // AQUA-BLUE metrage x4.0, misc GLUE x4 (the HEAD=HS2 conditional RESIN line doesn't apply) -
    // per unit. Two in-house Expected units double every one of those.
    [Fact]
    public async Task ComputeAsync_AggregatesInHouseNeeds_ExcludesExternalAndCancelled_ListsUnresolved()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");

        int matSupplierId, extSupplierId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var matSupplier = new Supplier { Code = "MATSUP", Name = "Materials Co" };
            var extSupplier = new Supplier { Code = "EXTSUP", Name = "External Co" };
            db.Suppliers.Add(matSupplier);
            db.Suppliers.Add(extSupplier);
            await db.SaveChangesAsync();
            matSupplierId = matSupplier.Id;
            extSupplierId = extSupplier.Id;

            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = "FJORD" }); // in-house marker
            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = extSupplierId, ModelCode = "FJORD-STUDIO" }); // mapped external
            await db.SaveChangesAsync();

            var draftMo = new MaterialOrder { Number = "MO-2026-0001", SupplierId = matSupplierId, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow };
            draftMo.Lines.Add(new MaterialOrderLine { Kind = MaterialKind.Foam, Code = "FM-STD", QuantityOrdered = 5m, QuantityReceived = 2m });
            db.Add(draftMo);
            var completedMo = new MaterialOrder { Number = "MO-2026-0002", SupplierId = matSupplierId, CreatedByUserId = "office-1", CreatedAt = DateTime.UtcNow, State = MaterialOrderState.Completed };
            completedMo.Lines.Add(new MaterialOrderLine { Kind = MaterialKind.Foam, Code = "FM-STD", QuantityOrdered = 100m, QuantityReceived = 100m });
            db.Add(completedMo);
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "FM-STD", Amount = 1m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var (inHouseOrderId, inHouseLineId) = await SeedOrderLineAsync(factory, "FJORD", "FJ2", "1");
        await SeedUnitAsync(factory, inHouseOrderId, inHouseLineId, 1);
        await SeedUnitAsync(factory, inHouseOrderId, inHouseLineId, 2);
        await SeedUnitAsync(factory, inHouseOrderId, inHouseLineId, 3, ProductionUnitState.Cancelled); // excluded by state

        var (externalOrderId, externalLineId) = await SeedOrderLineAsync(factory, "FJORD-STUDIO", "FS2", "1");
        await SeedUnitAsync(factory, externalOrderId, externalLineId, 1); // mapped external - excluded silently

        var (unresolvedOrderId, unresolvedLineId) = await SeedOrderLineAsync(factory, "GHOST", "FJ2", "1");
        await SeedUnitAsync(factory, unresolvedOrderId, unresolvedLineId, 1); // no map row - unresolved

        var (dropshipOrderId, dropshipLineId) = await SeedOrderLineAsync(factory, "FJORD-STUDIO", "FS2", "1", lineSupplierId: extSupplierId);
        await SeedUnitAsync(factory, dropshipOrderId, dropshipLineId, 1); // dropship-pinned - excluded silently, not unresolved

        var service = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), NewOutputRoot());

        var forecast = await service.ComputeAsync();

        Assert.Equal(["GHOST"], forecast.UnresolvedModelCodes);
        Assert.Equal(5, forecast.Rows.Count);
        Assert.Equal([MaterialKind.Foam, MaterialKind.Frame, MaterialKind.Cotton, MaterialKind.Fabric, MaterialKind.Misc],
            forecast.Rows.Select(r => r.Kind).ToArray());

        var foam = forecast.Rows.Single(r => r.Kind == MaterialKind.Foam);
        Assert.Equal("FM-STD", foam.Code);
        Assert.Null(foam.HardnessCode);
        Assert.Equal("Foam Standard", foam.DisplayName);
        Assert.Equal(4m, foam.GrossNeed);
        Assert.Equal(1m, foam.InStock);
        Assert.Equal(-3m, foam.StockAfterNeeds);
        Assert.Equal(3m, foam.OnOrder); // draft remainder (5-2) only - completed order excluded
        Assert.Equal(0m, foam.SuggestedToOrder); // max(0, 4-1-3)

        var frame = forecast.Rows.Single(r => r.Kind == MaterialKind.Frame);
        Assert.Equal("FBX", frame.Code);
        Assert.Equal("FBX", frame.DisplayName); // FrameBody carries no name master
        Assert.Equal(2m, frame.GrossNeed);
        Assert.Equal(0m, frame.InStock);
        Assert.Equal(-2m, frame.StockAfterNeeds);
        Assert.Equal(0m, frame.OnOrder);
        Assert.Equal(2m, frame.SuggestedToOrder);

        var cotton = forecast.Rows.Single(r => r.Kind == MaterialKind.Cotton);
        Assert.Equal("COT-STD", cotton.Code);
        Assert.Equal("Cotton Standard", cotton.DisplayName);
        Assert.Equal(6.0m, cotton.GrossNeed);
        Assert.Equal(6.0m, cotton.SuggestedToOrder);

        var fabric = forecast.Rows.Single(r => r.Kind == MaterialKind.Fabric);
        Assert.Equal("AQUA-BLUE", fabric.Code);
        Assert.Equal("Aqua Blue", fabric.DisplayName); // resolved from the AQUA fabric group's colour master
        Assert.Equal(8.0m, fabric.GrossNeed);
        Assert.Equal(8.0m, fabric.SuggestedToOrder);

        var misc = forecast.Rows.Single(r => r.Kind == MaterialKind.Misc);
        Assert.Equal("GLUE", misc.Code);
        Assert.Equal("Glue", misc.DisplayName);
        Assert.Equal(8m, misc.GrossNeed);
        Assert.Equal(8m, misc.SuggestedToOrder);
    }

    // Same Kind (Fabric), two different Codes: seeded in reverse-alphabetical order to prove the
    // row sort is a genuine ordinal-Code tie-break, not insertion order.
    [Fact]
    public async Task ComputeAsync_OrdersRowsWithinKindByCodeOrdinal()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = "FJORD" });
            await db.SaveChangesAsync();
        }

        var (greenOrderId, greenLineId) = await SeedOrderLineAsync(factory, "FJORD", "FJ2", "1", fabricColorCode: "AQUA-GREEN");
        await SeedUnitAsync(factory, greenOrderId, greenLineId, 1);
        var (blueOrderId, blueLineId) = await SeedOrderLineAsync(factory, "FJORD", "FJ2", "1", fabricColorCode: "AQUA-BLUE");
        await SeedUnitAsync(factory, blueOrderId, blueLineId, 1);

        var service = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), NewOutputRoot());

        var forecast = await service.ComputeAsync();

        var fabricCodes = forecast.Rows.Where(r => r.Kind == MaterialKind.Fabric).Select(r => r.Code).ToArray();
        Assert.Equal(["AQUA-BLUE", "AQUA-GREEN"], fabricCodes);
    }

    // Materials 1: previously silent (the group with a null PinnedVersion key was just skipped in
    // the aggregation loop) - now surfaced as its own list, mirroring UnresolvedModelCodes, so an
    // in-house unit that can't resolve material needs yet doesn't just vanish from the forecast.
    [Fact]
    public async Task ComputeAsync_UnpinnedInHouseUnit_SurfacedInUnpinnedUnitCodes_NotUnresolved()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = "FJORD" });
            await db.SaveChangesAsync();
        }

        var (orderId, lineId) = await SeedOrderLineAsync(factory, "FJORD", "FJ2", pinnedVersion: null);
        await SeedUnitAsync(factory, orderId, lineId, 1);
        var unitCode = $"ORD-{orderId}-1";

        var service = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), NewOutputRoot());

        var forecast = await service.ComputeAsync();

        Assert.Equal([unitCode], forecast.UnpinnedUnitCodes);
        Assert.Empty(forecast.UnresolvedModelCodes);
        Assert.Empty(forecast.Rows);
    }

    [Fact]
    public async Task ComputeAsync_NoInHouseUnits_ReturnsEmptyForecast()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), NewOutputRoot());

        var forecast = await service.ComputeAsync();

        Assert.Empty(forecast.Rows);
        Assert.Empty(forecast.UnresolvedModelCodes);
    }

    [Fact]
    public async Task ComputeAsync_RejectsMechanicAndWarehouse()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        foreach (var role in new[] { Roles.Mechanic, Roles.Warehouse })
        {
            var service = new MaterialNeedsService(factory, new FakeCurrentUser("intruder", role), new PinnedCatalogueProvider(factory), NewOutputRoot());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ComputeAsync());
        }
    }

    [Fact]
    public async Task ExportCsvAsync_WritesSemicolonInvariantFile_WithClockPinnedName()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        SeedPublishedCatalogue(factory, "1");
        var (orderId, lineId) = await SeedOrderLineAsync(factory, "FJORD", "FJ2", "1");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = "FJORD" });
            await db.SaveChangesAsync();
        }
        await SeedUnitAsync(factory, orderId, lineId, 1);
        var outputRoot = NewOutputRoot();
        var pinnedNow = new DateTime(2026, 8, 12, 10, 30, 0, DateTimeKind.Utc);
        var service = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), outputRoot, () => pinnedNow);
        var forecast = await service.ComputeAsync();

        var filePath = await service.ExportCsvAsync(forecast);

        Assert.Equal(Path.Combine(outputRoot, "material-needs-20260812-103000.csv"), filePath);
        Assert.True(File.Exists(filePath));
        var lines = (await File.ReadAllLinesAsync(filePath)).Where(l => l.Length > 0).ToArray();
        Assert.Equal("Kind;Code;Hardness;Name;GrossNeed;InStock;StockAfterNeeds;OnOrder;SuggestedToOrder", lines[0]);

        var dataLines = lines.Skip(1).ToArray();
        Assert.NotEmpty(dataLines);
        var commaDecimal = new Regex(@"\d+,\d");
        foreach (var line in dataLines)
        {
            Assert.Equal(9, line.Split(';').Length);
            Assert.DoesNotMatch(commaDecimal, line);
        }
        Assert.Contains(dataLines, l => l.StartsWith("Frame;FBX;;FBX;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StockAsync_ReturnsRows_OrderedByKindThenCodeOrdinal()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "FM-Z", Amount = 1m, UpdatedAt = DateTime.UtcNow });
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Foam, Code = "FM-A", Amount = 2m, UpdatedAt = DateTime.UtcNow });
            db.MaterialStocks.Add(new MaterialStock { Kind = MaterialKind.Cotton, Code = "COT-STD", Amount = 3m, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var service = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), NewOutputRoot());

        var stock = await service.StockAsync();

        // MaterialKind's declaration order is Foam, Frame, Cotton, Fabric, Misc - Foam sorts before
        // Cotton by that underlying enum value, not alphabetically.
        Assert.Equal(
            [(MaterialKind.Foam, "FM-A"), (MaterialKind.Foam, "FM-Z"), (MaterialKind.Cotton, "COT-STD")],
            stock.Select(s => (s.Kind, s.Code)).ToArray());
    }

    [Fact]
    public async Task AdjustStockAsync_InsertsNewRow_ThenUpsertsAbsoluteAmountOnSameRow()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new MaterialNeedsService(factory, OfficeUser, new PinnedCatalogueProvider(factory), NewOutputRoot());

        await service.AdjustStockAsync(MaterialKind.Foam, "FM-STD", "H35", 12m);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var stock = await db.MaterialStocks.SingleAsync(s => s.Kind == MaterialKind.Foam && s.Code == "FM-STD" && s.HardnessCode == "H35");
            Assert.Equal(12m, stock.Amount);

            // First adjustment: old amount 0 (no prior row) -> new 12, delta +12 (raise). Same
            // SaveChanges as the stock insert; reference null (an adjustment carries no order/unit).
            var movement = await db.MaterialMovements.SingleAsync();
            Assert.Equal(MaterialMovementType.Adjustment, movement.Type);
            Assert.Equal(12m, movement.Quantity);
            Assert.Null(movement.Reference);
        }

        // Second call on the same (Kind, Code, HardnessCode) upserts the existing row absolutely -
        // not additively, and never creates a second row.
        await service.AdjustStockAsync(MaterialKind.Foam, "FM-STD", "H35", -4m);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var stock = await db.MaterialStocks.SingleAsync(s => s.Kind == MaterialKind.Foam && s.Code == "FM-STD" && s.HardnessCode == "H35");
            Assert.Equal(-4m, stock.Amount);
            Assert.Equal(1, await db.MaterialStocks.CountAsync());

            // Second adjustment: old amount 12 -> new -4, delta -16 (lower). A second movement row
            // is appended (unlike the stock balance, the log never upserts).
            var movements = await db.MaterialMovements.OrderBy(m => m.Id).ToListAsync();
            Assert.Equal(2, movements.Count);
            Assert.Equal(-16m, movements[1].Quantity);
        }
    }

    [Fact]
    public async Task AdjustStockAsync_RejectsMechanicAndWarehouse()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        foreach (var role in new[] { Roles.Mechanic, Roles.Warehouse })
        {
            var service = new MaterialNeedsService(factory, new FakeCurrentUser("intruder", role), new PinnedCatalogueProvider(factory), NewOutputRoot());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustStockAsync(MaterialKind.Foam, "FM-STD", null, 1m));
        }
    }
}
