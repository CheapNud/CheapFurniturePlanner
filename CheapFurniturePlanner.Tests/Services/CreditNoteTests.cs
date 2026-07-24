using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// SQLite harness mirrors InvoicingServiceTests: in-memory SQLite, migrated schema. Invoice
// fixtures are built through the real CreateInvoiceAsync so credit-note math is exercised
// against genuine snapshot totals, not hand-built Invoice rows.
public class CreditNoteTests
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
    public async Task FullCredit_TakesRemainingBalance_AndNumbers()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var service = NewService(factory, OfficeUser);
        var invoice = await service.CreateInvoiceAsync(orderId);

        var credit = await service.CreateCreditNoteAsync(invoice.Id, CreditReason.Return);

        Assert.Equal(invoice.GrossTotal, credit.GrossAmount);
        Assert.Equal($"CN-{DateTime.UtcNow.Year}-0001", credit.CreditNoteNumber);

        // Remaining balance is now 0 - a second full credit has nothing left to take.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCreditNoteAsync(invoice.Id, CreditReason.Return));
    }

    [Fact]
    public async Task ManualCredit_SplitsAtBlendedRate()
    {
        // Invoice NetTotal 100, VatTotal 21, GrossTotal 121 -> blended VAT share is 21/121.
        // Credit gross 12.10: vatShare = 21/121, vatAmount = Round(12.10 * 21/121, 2, AwayFromZero).
        // 12.10 * 21 = 254.10; 254.10 / 121 = 2.1000... -> rounds to 2.10 exactly.
        // netAmount = gross - vatAmount = 12.10 - 2.10 = 10.00.
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var service = NewService(factory, OfficeUser);
        var invoice = await service.CreateInvoiceAsync(orderId);
        Assert.Equal(100m, invoice.NetTotal);
        Assert.Equal(21m, invoice.VatTotal);
        Assert.Equal(121m, invoice.GrossTotal);

        var credit = await service.CreateCreditNoteAsync(invoice.Id, CreditReason.PriceCorrection, 12.10m);

        Assert.Equal(2.10m, credit.VatAmount);
        Assert.Equal(10.00m, credit.NetAmount);
        Assert.Equal(12.10m, credit.GrossAmount);
    }

    [Fact]
    public async Task OverCredit_AndNonPositive_Rejected()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var service = NewService(factory, OfficeUser);
        var invoice = await service.CreateInvoiceAsync(orderId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCreditNoteAsync(invoice.Id, CreditReason.Return, 121.01m));
        Assert.Contains("121.00", ex.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCreditNoteAsync(invoice.Id, CreditReason.Return, 0m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCreditNoteAsync(invoice.Id, CreditReason.Return, -1m));
    }

    [Fact]
    public async Task PartialThenFull_HonorsPriorCredits()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var service = NewService(factory, OfficeUser);
        var invoice = await service.CreateInvoiceAsync(orderId);

        await service.CreateCreditNoteAsync(invoice.Id, CreditReason.Goodwill, 10.00m);
        var secondCredit = await service.CreateCreditNoteAsync(invoice.Id, CreditReason.Return);

        Assert.Equal(invoice.GrossTotal - 10.00m, secondCredit.GrossAmount);
    }

    [Fact]
    public async Task ZeroGrossInvoice_CannotBeCredited()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 0m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (0m, 1, 0m, 0m));
        var service = NewService(factory, OfficeUser);
        var invoice = await service.CreateInvoiceAsync(orderId);
        Assert.Equal(0m, invoice.GrossTotal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCreditNoteAsync(invoice.Id, CreditReason.Return));
    }

    [Fact]
    public async Task Settle_StampsOnce_AndGuards()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        await SeedVatAsync(factory, "BE", 21m);
        var orderId = await SeedPlacedOrderAsync(factory, 0m, "BE", (100m, 1, 100m, 0m));
        var service = NewService(factory, OfficeUser);
        var invoice = await service.CreateInvoiceAsync(orderId);
        var credit = await service.CreateCreditNoteAsync(invoice.Id, CreditReason.Return);

        await service.MarkSettledAsync(credit.Id);

        var reloaded = await service.GetInvoiceAsync(invoice.Id);
        var settled = Assert.Single(reloaded!.CreditNotes);
        Assert.True(settled.IsSettled);
        Assert.NotNull(settled.SettledAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkSettledAsync(credit.Id));

        var mechanicService = NewService(factory, new FakeCurrentUser("wrench", Roles.Mechanic));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mechanicService.CreateCreditNoteAsync(invoice.Id, CreditReason.Return));
    }
}
