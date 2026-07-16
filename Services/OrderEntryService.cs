using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public sealed class OrderPlacedException(string orderNumber) : Exception($"Order '{orderNumber}' is not a draft; it cannot be modified.");

// All order mutations live here; pages never touch the context directly. The first successful
// AddLine pins the order to the currently effective published version; every later resolution and
// price uses that pinned bundle (PinnedCatalogueProvider), never "current".
public sealed class OrderEntryService(
    IDbContextFactory<FurniturePlannerContext> factory,
    ICatalogueSource catalogue,
    PinnedCatalogueProvider pinned)
{
    public async Task<Order> CreateOrderAsync(int sellerId, int consumerId, string marketCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(marketCode)) { throw new InvalidOperationException("Market is required."); }
        await using var db = await factory.CreateDbContextAsync(ct);
        var year = DateTime.UtcNow.Year;
        var prefix = $"ORD-{year}-";
        var countThisYear = await db.Orders.CountAsync(o => o.OrderNumber.StartsWith(prefix), ct);
        var order = new Order
        {
            OrderNumber = $"{prefix}{countThisYear + 1:D4}",
            SellerId = sellerId,
            ConsumerId = consumerId,
            MarketCode = marketCode.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<Order?> GetOrderAsync(int orderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Orders.AsNoTracking()
            .Include(o => o.Seller).Include(o => o.Consumer)
            .Include(o => o.Lines.OrderBy(l => l.DisplayIndex))
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
    }

    public async Task<List<Order>> ListOrdersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Orders.AsNoTracking()
            .Include(o => o.Seller).Include(o => o.Consumer).Include(o => o.Lines)
            .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
    }

    public decimal OrderTotal(Order order) => order.Lines.Sum(l => l.LineTotal);

    // Resolves the snapshot this order works against: the pinned version once one exists, else the
    // currently effective one (which the first AddLine will pin).
    public async Task<CatalogueSnapshot> SnapshotForAsync(Order order, CancellationToken ct = default)
        => order.PinnedCatalogueVersion is null
            ? await catalogue.GetCurrentAsync()
            : await pinned.GetAsync(order.PinnedCatalogueVersion, ct);

    public async Task AddStandaloneLineAsync(int orderId, int articleId, int quantity, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await RequireDraftAsync(db, orderId, ct);
        var snapshot = await SnapshotForAsync(order, ct);
        var article = snapshot.Articles.FirstOrDefault(a => a.Id == articleId && !a.IsCatalogueBacked())
            ?? throw new InvalidOperationException($"Standalone article {articleId} is not in this order's catalogue.");
        if (article.ManualPrice is null) { throw new InvalidOperationException($"Article '{article.AssignedCode}' has no manual price."); }
        if (quantity < 1) { throw new InvalidOperationException("Quantity must be at least 1."); }
        Pin(order, snapshot);
        var unitPrice = article.ManualPrice.Value;
        order.Lines.Add(new OrderLine
        {
            DisplayIndex = order.Lines.Count,
            Kind = OrderLineKind.StandaloneArticle,
            ArticleId = article.Id,
            AssignedCode = article.AssignedCode,
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * quantity,
            SupplierRef = article.SupplierRef,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateQuantityAsync(int orderId, int lineId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 1) { throw new InvalidOperationException("Quantity must be at least 1."); }
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await RequireDraftAsync(db, orderId, ct);
        var line = order.Lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException($"Order line {lineId} not found.");
        line.Quantity = quantity;
        line.LineTotal = line.UnitPrice * quantity;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveLineAsync(int orderId, int lineId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await RequireDraftAsync(db, orderId, ct);
        var line = order.Lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException($"Order line {lineId} not found.");
        order.Lines.Remove(line);
        for (var index = 0; index < order.Lines.Count; index++) { order.Lines[index].DisplayIndex = index; }
        await db.SaveChangesAsync(ct);
    }

    // The pin: stamped by the first line, immutable afterwards (removing the last line keeps it).
    private static void Pin(Order order, CatalogueSnapshot snapshot)
    {
        order.PinnedCatalogueVersion ??= snapshot.Version;
        order.PinnedContentHash ??= snapshot.ContentHash;
    }

    private static async Task<Order> RequireDraftAsync(FurniturePlannerContext db, int orderId, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Lines.OrderBy(l => l.DisplayIndex)).FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        if (order.State != OrderState.Draft) { throw new OrderPlacedException(order.OrderNumber); }
        return order;
    }
}
