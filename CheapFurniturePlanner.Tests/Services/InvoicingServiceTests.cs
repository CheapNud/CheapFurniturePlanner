using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors ServiceTicketServiceTests: in-memory SQLite, migrated schema.
public class InvoicingServiceTests
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

    private static InvoicingService NewService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser who) => new(factory, who);
    private static readonly FakeCurrentUser OfficeUser = new("office-1", Roles.Office);

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

    [Fact]
    public async Task Create_SnapshotsStoredValues_AndNumbersSequentially()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var firstOrderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var secondOrderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (50m, 2, 25m, 0m));
        var service = NewService(factory, OfficeUser);

        var first = await service.CreateInvoiceAsync(firstOrderId);
        var second = await service.CreateInvoiceAsync(secondOrderId);

        Assert.Equal($"INV-{DateTime.UtcNow.Year}-0001", first.InvoiceNumber);
        Assert.Equal($"INV-{DateTime.UtcNow.Year}-0002", second.InvoiceNumber);
        Assert.Equal("office-1", first.CreatedByUserId);

        var loaded = await service.GetInvoiceAsync(first.Id);
        var line = Assert.Single(loaded!.Lines);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(0m, line.DiscountPercent);
        Assert.Equal(100m, line.LineTotal);
        Assert.Equal(21m, line.VatRatePercent);
    }

    [Fact]
    public async Task Create_LegacyFlatSumBug_NeverReturns()
    {
        // Line discount 10% is already baked into the stored LineTotal (90 on UnitPrice 100, qty 1 -
        // this mirrors a manually-overridden discount line). Order discount is a further 10% cascade.
        // Cascade: 90 * (1 - 0.10) = 81.00
        // Legacy flat-sum bug computed UnitPrice*Qty*(1 - lineDiscount - orderDiscount) = 100*(1-0.20) = 80.00.
        // VAT rate 0 isolates NetTotal from any VAT rounding noise.
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "X0", 0m);
        var orderId = await SeedPlacedOrderAsync(factory, 10m, "X0", (90m, 1, 100m, 10m));
        var service = NewService(factory, OfficeUser);

        var invoice = await service.CreateInvoiceAsync(orderId);

        Assert.Equal(81.00m, invoice.NetTotal);
        Assert.NotEqual(80.00m, invoice.NetTotal);
    }

    [Fact]
    public async Task Create_ParityWithOrderTotal()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 7.5m, "BE",
            (33.33m, 1, 33.33m, 0m), (66.67m, 2, 33.335m, 0m), (199.99m, 3, 66.663m, 0m));
        var service = NewService(factory, OfficeUser);

        var invoice = await service.CreateInvoiceAsync(orderId);

        var order = await service.GetInvoiceAsync(invoice.Id);
        var lineTotals = new[] { 33.33m, 66.67m, 199.99m };
        var expectedNet = Math.Round(lineTotals.Sum() * (1 - 0.075m), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(expectedNet, invoice.NetTotal);

        var expectedVat = lineTotals
            .Select(lineTotal => Math.Round(lineTotal * (1 - 0.075m) * 21m / 100m, 2, MidpointRounding.AwayFromZero))
            .Sum();
        Assert.Equal(expectedVat, invoice.VatTotal);
        Assert.Equal(invoice.NetTotal + invoice.VatTotal, invoice.GrossTotal);
    }

    [Fact]
    public async Task Create_VatRounding_PerLine_AwayFromZero()
    {
        // LineTotal 0.25, no order/line discount, VAT rate 10%: lineNet = 0.25, vat = 0.25*0.10 = 0.025.
        // AwayFromZero rounds 0.025 -> 0.03; banker's rounding (ToEven) would give 0.02 (2 is even).
        // This is the case that distinguishes the two rounding modes.
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 10m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (0.25m, 1, 0.25m, 0m));
        var service = NewService(factory, OfficeUser);

        var invoice = await service.CreateInvoiceAsync(orderId);

        var loaded = await service.GetInvoiceAsync(invoice.Id);
        var line = Assert.Single(loaded!.Lines);
        Assert.Equal(0.03m, line.VatAmount);
        Assert.Equal(0.03m, invoice.VatTotal);
    }

    private static async Task<int> SeedOrderInStateAsync(IDbContextFactory<FurniturePlannerContext> factory, OrderState state, string marketCode = "BE")
    {
        await using var db = await factory.CreateDbContextAsync();
        var seller = new Seller { Name = "Shop" };
        var consumer = new Consumer { Name = "Jansen" };
        db.Sellers.Add(seller);
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        var order = new Order { OrderNumber = $"ORD-2026-{db.Orders.Count() + 1:D4}", SellerId = seller.Id, ConsumerId = consumer.Id, MarketCode = marketCode, State = state };
        order.Lines.Add(new OrderLine { Kind = OrderLineKind.ConfiguredElement, DisplayIndex = 0, Quantity = 1, UnitPrice = 10m, LineTotal = 10m, VariantCode = "K7E:V1" });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task Create_Guards()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var service = NewService(factory, OfficeUser);

        var draftOrderId = await SeedOrderInStateAsync(factory, OrderState.Draft);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInvoiceAsync(draftOrderId));

        var cancelledOrderId = await SeedOrderInStateAsync(factory, OrderState.Cancelled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInvoiceAsync(cancelledOrderId));

        var placedOrderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (10m, 1, 10m, 0m));
        await service.CreateInvoiceAsync(placedOrderId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInvoiceAsync(placedOrderId));

        var noVatOrderId = await SeedPlacedOrderAsync(factory, 0m, "ZZ", (10m, 1, 10m, 0m));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInvoiceAsync(noVatOrderId));
        Assert.Contains("ZZ", ex.Message);

        var warehouseOnlyOrderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (10m, 1, 10m, 0m));
        var warehouseService = NewService(factory, new FakeCurrentUser("wh-1", Roles.Warehouse));
        await Assert.ThrowsAsync<InvalidOperationException>(() => warehouseService.CreateInvoiceAsync(warehouseOnlyOrderId));
    }

    [Fact]
    public async Task VatRates_UpsertAndDelete()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = NewService(factory, OfficeUser);

        await service.SetVatRateAsync("BE", 21m);
        var afterInsert = await service.VatRatesAsync();
        var beRate = Assert.Single(afterInsert);
        Assert.Equal("BE", beRate.MarketCode);
        Assert.Equal(21m, beRate.RatePercent);

        // "be " (different case + trailing space) is trimmed but NOT case-normalized, so it is an
        // exact match only against "be" - since the stored code is "BE", this must insert a second
        // distinct row rather than updating the existing one.
        await service.SetVatRateAsync("be ", 6m);
        var afterSecondInsert = await service.VatRatesAsync();
        Assert.Equal(2, afterSecondInsert.Count);

        // Now update the SAME code exactly ("BE") and confirm it updates in place, not inserts.
        await service.SetVatRateAsync("BE", 12m);
        var afterUpdate = await service.VatRatesAsync();
        Assert.Equal(2, afterUpdate.Count);
        Assert.Equal(12m, afterUpdate.Single(r => r.MarketCode == "BE").RatePercent);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetVatRateAsync("FR", 101m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetVatRateAsync("FR", -1m));

        var toDelete = afterUpdate.Single(r => r.MarketCode == "BE");
        await service.DeleteVatRateAsync(toDelete.Id);
        var afterDelete = await service.VatRatesAsync();
        Assert.DoesNotContain(afterDelete, r => r.MarketCode == "BE");

        var mechanicService = NewService(factory, new FakeCurrentUser("wrench", Roles.Mechanic));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mechanicService.SetVatRateAsync("NL", 21m));
    }

    [Fact]
    public async Task MarkPaid_StampsOnce()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (10m, 1, 10m, 0m));
        var service = NewService(factory, OfficeUser);
        var invoice = await service.CreateInvoiceAsync(orderId);

        await service.MarkPaidAsync(invoice.Id);

        var loaded = await service.GetInvoiceAsync(invoice.Id);
        Assert.True(loaded!.IsPaid);
        Assert.NotNull(loaded.PaidAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkPaidAsync(invoice.Id));
    }
}
