using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Minimal party management for order entry: Sellers (who place orders; Multiplier feeds
// PricingContext.SellerMultiplier) and Consumers (who receive them). Deliberately thin —
// discounts, addresses and the fuller four-party model are later phases.
public sealed class PartyService(IDbContextFactory<FurniturePlannerContext> factory)
{
    public async Task<List<Seller>> SellersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Sellers.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task<List<Consumer>> ConsumersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Consumers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<Seller> AddSellerAsync(string name, decimal multiplier, CancellationToken ct = default)
    {
        RequireName(name);
        if (multiplier <= 0) { throw new InvalidOperationException("Seller multiplier must be positive."); }
        await using var db = await factory.CreateDbContextAsync(ct);
        var seller = new Seller { Name = name.Trim(), Multiplier = multiplier };
        db.Sellers.Add(seller);
        await db.SaveChangesAsync(ct);
        return seller;
    }

    public async Task<Consumer> AddConsumerAsync(string name, string? contact, CancellationToken ct = default)
    {
        RequireName(name);
        await using var db = await factory.CreateDbContextAsync(ct);
        var consumer = new Consumer { Name = name.Trim(), Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim() };
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync(ct);
        return consumer;
    }

    public async Task UpdateSellerAsync(int id, string name, decimal multiplier, CancellationToken ct = default)
    {
        RequireName(name);
        if (multiplier <= 0) { throw new InvalidOperationException("Seller multiplier must be positive."); }
        await using var db = await factory.CreateDbContextAsync(ct);
        var seller = await db.Sellers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException($"Seller {id} not found.");
        seller.Name = name.Trim();
        seller.Multiplier = multiplier;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateConsumerAsync(int id, string name, string? contact, CancellationToken ct = default)
    {
        RequireName(name);
        await using var db = await factory.CreateDbContextAsync(ct);
        var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Consumer {id} not found.");
        consumer.Name = name.Trim();
        consumer.Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSellerAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Orders.AnyAsync(o => o.SellerId == id, ct))
        {
            throw new InvalidOperationException("Cannot delete a seller with orders.");
        }
        await db.Sellers.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteConsumerAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Orders.AnyAsync(o => o.ConsumerId == id, ct))
        {
            throw new InvalidOperationException("Cannot delete a consumer with orders.");
        }
        await db.Consumers.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
    }

    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { throw new InvalidOperationException("Name is required."); }
    }
}
