using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 4: MaterialOrder's own lifecycle (Draft -> Sent -> Completed), independent of SupplierOrder
// - a material need has no ProductionUnit to double as its line, so receipt applies straight to a
// stored MaterialOrderLine and, in the same SaveChanges, to a MaterialStock balance. Harness mirrors
// PurchasingServiceTests: in-memory SQLite, migrated schema, FakeCurrentUser, direct-EF seeding.
public class MaterialOrderServiceTests
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

    private static async Task<int> SeedSupplierAsync(IDbContextFactory<FurniturePlannerContext> factory, string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        var supplier = new Supplier { Code = code, Name = code };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    private static MaterialOrderLine FoamLine(decimal ordered) =>
        new() { Kind = MaterialKind.Foam, Code = "F-100", HardnessCode = "H35", DisplayName = "Foam H35", QuantityOrdered = ordered };

    [Fact]
    public async Task CreateDraft_NumbersWithMpoPrefix_AcceptsEmptyLines()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);

        var order = await materials.CreateDraftAsync(supplierId, []);

        Assert.StartsWith($"MPO-{DateTime.UtcNow.Year}-", order.Number);
        Assert.Equal(MaterialOrderState.Draft, order.State);
        Assert.Empty(order.Lines);
    }

    [Fact]
    public async Task CreateDraft_WithLines_PersistsThem()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);

        var order = await materials.CreateDraftAsync(supplierId, [FoamLine(20m)]);

        var reloaded = await materials.GetAsync(order.Id);
        var line = Assert.Single(reloaded!.Lines);
        Assert.Equal("F-100", line.Code);
        Assert.Equal(20m, line.QuantityOrdered);
    }

    [Fact]
    public async Task Numbering_SurvivesDeletedDraft_NoCollision()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);

        var first = await materials.CreateDraftAsync(supplierId, []);
        var second = await materials.CreateDraftAsync(supplierId, []);
        await materials.DeleteDraftAsync(first.Id);
        var third = await materials.CreateDraftAsync(supplierId, []);

        // A naive count-based scheme (1 live draft left after the delete -> count+1) would reissue
        // second's own suffix here. Max-suffix numbering must always clear the highest live one.
        var suffixSecond = int.Parse(second.Number.Split('-')[^1]);
        var suffixThird = int.Parse(third.Number.Split('-')[^1]);
        Assert.True(suffixThird > suffixSecond, $"expected {third.Number} suffix to exceed {second.Number} suffix");
        Assert.Null(await materials.GetAsync(first.Id));
    }

    // Regression: a Sent order with a 0-qty line can never complete (ReceiveAsync rejects any
    // receipt against a zero remainder) and a Sent order is neither editable nor deletable -
    // permanently stuck. Both line entry points must reject it up front.
    [Fact]
    public async Task CreateDraft_RejectsZeroOrNegativeQuantityLine()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);

        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.CreateDraftAsync(supplierId, [FoamLine(0m)]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.CreateDraftAsync(supplierId, [FoamLine(-1m)]));
    }

    [Fact]
    public async Task AddLine_RejectsZeroOrNegativeQuantityLine()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.AddLineAsync(order.Id, FoamLine(0m)));
        Assert.Empty((await materials.GetAsync(order.Id))!.Lines);
    }

    [Fact]
    public async Task AddLine_RemoveLine_OnlyOnDraft()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId, []);

        await materials.AddLineAsync(order.Id, FoamLine(10m));
        var withLine = await materials.GetAsync(order.Id);
        var line = Assert.Single(withLine!.Lines);

        await materials.RemoveLineAsync(order.Id, line.Id);
        var withoutLine = await materials.GetAsync(order.Id);
        Assert.Empty(withoutLine!.Lines);

        await materials.AddLineAsync(order.Id, FoamLine(10m));
        await materials.SendAsync(order.Id);
        var sentLine = Assert.Single((await materials.GetAsync(order.Id))!.Lines);
        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.AddLineAsync(order.Id, FoamLine(5m)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.RemoveLineAsync(order.Id, sentLine.Id));
    }

    [Fact]
    public async Task DeleteDraft_NonEmptyAllowed_CascadesLines_SentRejected()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId, [FoamLine(10m)]);

        // Draft deletable even non-empty - lines cascade (unlike SupplierOrder's empty-only guard,
        // MaterialOrderLine is a real stored row with nothing else pointing at it).
        await materials.DeleteDraftAsync(order.Id);
        Assert.Null(await materials.GetAsync(order.Id));

        var sentOrder = await materials.CreateDraftAsync(supplierId, [FoamLine(10m)]);
        await materials.SendAsync(sentOrder.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.DeleteDraftAsync(sentOrder.Id));
    }

    [Fact]
    public async Task Send_RequiresAtLeastOneLine_StampsSentAt_RejectsResend()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var emptyOrder = await materials.CreateDraftAsync(supplierId, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.SendAsync(emptyOrder.Id));

        var order = await materials.CreateDraftAsync(supplierId, [FoamLine(10m)]);
        await materials.SendAsync(order.Id);
        var sent = await materials.GetAsync(order.Id);
        Assert.Equal(MaterialOrderState.Sent, sent!.State);
        Assert.NotNull(sent.SentAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.SendAsync(order.Id));
    }

    [Fact]
    public async Task SetTheirReference_SentOrCompletedOnly()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId, [FoamLine(10m)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.SetTheirReferenceAsync(order.Id, "REF-1"));

        await materials.SendAsync(order.Id);
        await materials.SetTheirReferenceAsync(order.Id, "  REF-1  ");
        Assert.Equal("REF-1", (await materials.GetAsync(order.Id))!.TheirReference);

        var line = Assert.Single((await materials.GetAsync(order.Id))!.Lines);
        await materials.ReceiveAsync(order.Id, line.Id, 10m);
        var completed = await materials.GetAsync(order.Id);
        Assert.Equal(MaterialOrderState.Completed, completed!.State);

        // A fast-completing order (received in full on the first receipt) must still accept the
        // supplier's ref, same guarantee as PurchasingService.SetTheirReferenceAsync.
        await materials.SetTheirReferenceAsync(order.Id, "REF-2");
        Assert.Equal("REF-2", (await materials.GetAsync(order.Id))!.TheirReference);
    }

    [Fact]
    public async Task Receive_OnlyOnSent()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId, [FoamLine(10m)]);
        var line = Assert.Single(order.Lines);

        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.ReceiveAsync(order.Id, line.Id, 5m));

        await materials.SendAsync(order.Id);
        await materials.ReceiveAsync(order.Id, line.Id, 10m);
        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.ReceiveAsync(order.Id, line.Id, 1m)); // now Completed
    }

    [Fact]
    public async Task Receive_RejectsZeroNegativeAndOverReceipt()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId, [FoamLine(10m)]);
        var line = Assert.Single(order.Lines);
        await materials.SendAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.ReceiveAsync(order.Id, line.Id, 0m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.ReceiveAsync(order.Id, line.Id, -1m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.ReceiveAsync(order.Id, line.Id, 10.01m));

        // Every rejected receipt above throws before the stock/movement mutation - no orphaned
        // MaterialMovement row from a receipt that never actually landed.
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Empty(await db.MaterialMovements.ToListAsync());
        }

        await materials.ReceiveAsync(order.Id, line.Id, 6m);
        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.ReceiveAsync(order.Id, line.Id, 5m)); // remainder is 4

        // The rejected over-receipt above must not have appended a second movement onto the one
        // real receipt.
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.MaterialMovements.CountAsync());
        }
    }

    [Fact]
    public async Task Receive_IncrementsStock_UpsertsByKindCodeHardness_SameSaveChanges()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId, [FoamLine(20m)]);
        var line = Assert.Single(order.Lines);
        await materials.SendAsync(order.Id);

        await materials.ReceiveAsync(order.Id, line.Id, 6m);

        // Asserted immediately after ReceiveAsync returns - the stock mutation must land in the
        // same SaveChanges as the receipt, not a follow-up write.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var stock = await db.MaterialStocks.SingleAsync(s => s.Kind == MaterialKind.Foam && s.Code == "F-100" && s.HardnessCode == "H35");
            Assert.Equal(6m, stock.Amount);
            Assert.True(stock.UpdatedAt > DateTime.UtcNow.AddMinutes(-1));

            // Same guarantee for the movement row: exactly one Receipt movement, same SaveChanges,
            // referencing this order's number.
            var movement = await db.MaterialMovements.SingleAsync();
            Assert.Equal(MaterialKind.Foam, movement.Kind);
            Assert.Equal("F-100", movement.Code);
            Assert.Equal("H35", movement.HardnessCode);
            Assert.Equal(6m, movement.Quantity);
            Assert.Equal(MaterialMovementType.Receipt, movement.Type);
            Assert.Equal(order.Number, movement.Reference);
        }

        await materials.ReceiveAsync(order.Id, line.Id, 4m);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var stock = await db.MaterialStocks.SingleAsync(s => s.Kind == MaterialKind.Foam && s.Code == "F-100" && s.HardnessCode == "H35");
            Assert.Equal(10m, stock.Amount); // upserted onto the same row, not a second one
            Assert.Equal(1, await db.MaterialStocks.CountAsync());

            // Two receipts -> two movement rows (a log, unlike the upserted stock balance).
            var movements = await db.MaterialMovements.OrderBy(m => m.Id).ToListAsync();
            Assert.Equal(2, movements.Count);
            Assert.Equal(4m, movements[1].Quantity);
        }
    }

    [Fact]
    public async Task Receive_TwoPartials_CompletesOrder_OnlyWhenEveryLineFullyReceived()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierId, [FoamLine(10m), new MaterialOrderLine { Kind = MaterialKind.Cotton, Code = "COT-1", QuantityOrdered = 5m }]);
        var foamLine = order.Lines.Single(l => l.Kind == MaterialKind.Foam);
        var cottonLine = order.Lines.Single(l => l.Kind == MaterialKind.Cotton);
        await materials.SendAsync(order.Id);

        await materials.ReceiveAsync(order.Id, foamLine.Id, 10m); // first line fully received
        var stillSent = await materials.GetAsync(order.Id);
        Assert.Equal(MaterialOrderState.Sent, stillSent!.State); // second line still outstanding

        await materials.ReceiveAsync(order.Id, cottonLine.Id, 5m); // second (and last) line fully received
        var completed = await materials.GetAsync(order.Id);
        Assert.Equal(MaterialOrderState.Completed, completed!.State);
    }

    [Fact]
    public async Task List_ReturnsAllOrders_WithSupplierAndLines()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierId = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        await materials.CreateDraftAsync(supplierId, [FoamLine(10m)]);
        await materials.CreateDraftAsync(supplierId, []);

        var list = await materials.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.All(list, o => Assert.Equal("SUPA", o.Supplier!.Code));
    }

    // --- CreateDraftsByPreferredSupplierAsync (Task 4) ---

    private static async Task<int> SeedPreferredTermAsync(IDbContextFactory<FurniturePlannerContext> factory,
        MaterialKind kind, string code, string? hardnessCode, int supplierId, decimal unitPrice)
    {
        await using var db = await factory.CreateDbContextAsync();
        var term = new MaterialSupplierTerm
        {
            Kind = kind, Code = code, HardnessCode = hardnessCode, SupplierId = supplierId,
            UnitPrice = unitPrice, IsPreferred = true,
        };
        db.MaterialSupplierTerms.Add(term);
        await db.SaveChangesAsync();
        return term.Id;
    }

    [Fact]
    public async Task CreateDraftsByPreferredSupplier_GroupsTwoMaterials_TwoPreferredSuppliers_IntoTwoDrafts()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        var supplierB = await SeedSupplierAsync(factory, "SUPB");
        var materials = new MaterialOrderService(factory, OfficeUser);

        var rows = new[]
        {
            new MaterialOrderCandidate(MaterialKind.Foam, "F-100", "H35", "Foam H35", 20m, supplierA, 3.5m),
            new MaterialOrderCandidate(MaterialKind.Cotton, "COT-1", null, "Cotton wadding", 5m, supplierB, 1.2m),
        };

        var result = await materials.CreateDraftsByPreferredSupplierAsync(rows);

        Assert.Equal(2, result.CreatedOrderIds.Count);
        Assert.Empty(result.Unassigned);
        var orders = await materials.ListAsync();
        Assert.Equal(2, orders.Count);
        var orderA = orders.Single(o => o.Supplier!.Id == supplierA);
        var lineA = Assert.Single(orderA.Lines);
        Assert.Equal("F-100", lineA.Code);
        Assert.Equal(20m, lineA.QuantityOrdered);
        Assert.Equal(3.5m, lineA.UnitPrice);
        var orderB = orders.Single(o => o.Supplier!.Id == supplierB);
        var lineB = Assert.Single(orderB.Lines);
        Assert.Equal("COT-1", lineB.Code);
        Assert.Equal(1.2m, lineB.UnitPrice);
    }

    [Fact]
    public async Task CreateDraftsByPreferredSupplier_NoPreferredRow_ReturnedAsUnassigned_NotDrafted()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);

        var noPreferredRow = new MaterialOrderCandidate(MaterialKind.Frame, "FR-1", null, "Frame rail", 8m, null, null);
        var rows = new[]
        {
            new MaterialOrderCandidate(MaterialKind.Foam, "F-100", "H35", "Foam H35", 20m, supplierA, 3.5m),
            noPreferredRow,
        };

        var result = await materials.CreateDraftsByPreferredSupplierAsync(rows);

        Assert.Single(result.CreatedOrderIds);
        var unassigned = Assert.Single(result.Unassigned);
        Assert.Equal("FR-1", unassigned.Code);
        var orders = await materials.ListAsync();
        var order = Assert.Single(orders);
        Assert.Equal("F-100", Assert.Single(order.Lines).Code);
    }

    [Fact]
    public async Task CreateDraftsByPreferredSupplier_PriceSnapshot_ImmuneToLaterTermPriceChange()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        var termId = await SeedPreferredTermAsync(factory, MaterialKind.Foam, "F-100", "H35", supplierA, 3.5m);
        var materials = new MaterialOrderService(factory, OfficeUser);
        var planning = new MaterialPlanningService(factory, OfficeUser);

        var rows = new[] { new MaterialOrderCandidate(MaterialKind.Foam, "F-100", "H35", "Foam H35", 20m, supplierA, 3.5m) };
        var result = await materials.CreateDraftsByPreferredSupplierAsync(rows);

        // Term price changes after the draft was created - the already-created line must be unmoved.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var term = await db.MaterialSupplierTerms.SingleAsync(t => t.Id == termId);
            term.UnitPrice = 9.99m;
            await db.SaveChangesAsync();
        }

        var order = await materials.GetAsync(result.CreatedOrderIds[0]);
        Assert.Equal(3.5m, Assert.Single(order!.Lines).UnitPrice);
    }

    [Fact]
    public async Task CreateDraftsByPreferredSupplier_RejectsZeroOrNegativeQuantity_BeforeCreatingAnyDraft()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        var supplierB = await SeedSupplierAsync(factory, "SUPB");
        var materials = new MaterialOrderService(factory, OfficeUser);

        var rows = new[]
        {
            new MaterialOrderCandidate(MaterialKind.Foam, "F-100", "H35", "Foam H35", 20m, supplierA, 3.5m),
            new MaterialOrderCandidate(MaterialKind.Cotton, "COT-1", null, "Cotton wadding", 0m, supplierB, 1.2m),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => materials.CreateDraftsByPreferredSupplierAsync(rows));

        // Neither supplier's draft was created - the whole batch is rejected atomically.
        Assert.Empty(await materials.ListAsync());
    }

    // --- AddLineAsync price snapshot (Task 4) ---

    [Fact]
    public async Task AddLine_SnapshotsPreferredTermPrice_WhenLineHasNoExplicitPrice()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        await SeedPreferredTermAsync(factory, MaterialKind.Foam, "F-100", "H35", supplierA, 4.25m);
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierA, []);

        await materials.AddLineAsync(order.Id, FoamLine(10m));

        var line = Assert.Single((await materials.GetAsync(order.Id))!.Lines);
        Assert.Equal(4.25m, line.UnitPrice);
    }

    [Fact]
    public async Task AddLine_KeepsExplicitPrice_DoesNotOverwriteWithPreferredTerm()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        await SeedPreferredTermAsync(factory, MaterialKind.Foam, "F-100", "H35", supplierA, 4.25m);
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierA, []);

        var line = FoamLine(10m);
        line.UnitPrice = 2.00m;
        await materials.AddLineAsync(order.Id, line);

        var stored = Assert.Single((await materials.GetAsync(order.Id))!.Lines);
        Assert.Equal(2.00m, stored.UnitPrice);
    }

    [Fact]
    public async Task AddLine_NoPreferredTerm_LeavesPriceNull()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var supplierA = await SeedSupplierAsync(factory, "SUPA");
        var materials = new MaterialOrderService(factory, OfficeUser);
        var order = await materials.CreateDraftAsync(supplierA, []);

        await materials.AddLineAsync(order.Id, FoamLine(10m));

        var line = Assert.Single((await materials.GetAsync(order.Id))!.Lines);
        Assert.Null(line.UnitPrice);
    }
}
