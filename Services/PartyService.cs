using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Minimal party management for order entry: Sellers (who place orders; Multiplier feeds
// PricingContext.SellerMultiplier) and Consumers (who receive them). Grew to cover regions,
// suppliers and addresses (including a consumer's delivery-address book) - new mutations guard
// Admin/Office (same shape as ServiceTicketService); the original Seller/Consumer CRUD below
// stays unguarded as it was in Task 1.
public sealed class PartyService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser)
{
    public async Task<List<Seller>> SellersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Sellers.AsNoTracking()
            .Include(s => s.Address)!.ThenInclude(a => a!.Region)
            .OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task<List<Consumer>> ConsumersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Consumers.AsNoTracking()
            .Include(c => c.PrimaryAddress)!.ThenInclude(a => a!.Region)
            .OrderBy(c => c.Name).ToListAsync(ct);
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

    // --- Regions ---

    public async Task<List<Region>> RegionsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Regions.AsNoTracking().OrderBy(r => r.Code).ToListAsync(ct);
    }

    public async Task<Region> AddRegionAsync(string regionCode, string regionName, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var code = RequireTrimmed(regionCode, "Region code");
        var name = RequireTrimmed(regionName, "Region name");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Regions.AnyAsync(r => r.Code == code, ct))
        {
            throw new InvalidOperationException($"Region code '{code}' is already in use.");
        }
        var region = new Region { Code = code, Name = name };
        db.Regions.Add(region);
        await db.SaveChangesAsync(ct);
        return region;
    }

    public async Task UpdateRegionAsync(int regionId, string regionCode, string regionName, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var code = RequireTrimmed(regionCode, "Region code");
        var name = RequireTrimmed(regionName, "Region name");
        await using var db = await factory.CreateDbContextAsync(ct);
        var region = await db.Regions.FirstOrDefaultAsync(r => r.Id == regionId, ct)
            ?? throw new InvalidOperationException($"Region {regionId} not found.");
        if (await db.Regions.AnyAsync(r => r.Id != regionId && r.Code == code, ct))
        {
            throw new InvalidOperationException($"Region code '{code}' is already in use.");
        }
        region.Code = code;
        region.Name = name;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteRegionAsync(int regionId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var region = await db.Regions.FirstOrDefaultAsync(r => r.Id == regionId, ct)
            ?? throw new InvalidOperationException($"Region {regionId} not found.");
        if (await db.Addresses.AnyAsync(a => a.RegionId == regionId, ct))
        {
            throw new InvalidOperationException($"Region '{region.Code}' is used by addresses.");
        }
        db.Regions.Remove(region);
        await db.SaveChangesAsync(ct);
    }

    // --- Suppliers ---

    public async Task<List<Supplier>> SuppliersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Suppliers.AsNoTracking()
            .Include(s => s.Address)!.ThenInclude(a => a!.Region)
            .OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task<Supplier> AddSupplierAsync(string supplierCode, string supplierName, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var code = RequireTrimmed(supplierCode, "Supplier code");
        var name = RequireTrimmed(supplierName, "Supplier name");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Suppliers.AnyAsync(s => s.Code == code, ct))
        {
            throw new InvalidOperationException($"Supplier code '{code}' is already in use.");
        }
        var supplier = new Supplier { Code = code, Name = name };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);
        return supplier;
    }

    public async Task UpdateSupplierAsync(int supplierId, string supplierCode, string supplierName, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var code = RequireTrimmed(supplierCode, "Supplier code");
        var name = RequireTrimmed(supplierName, "Supplier name");
        await using var db = await factory.CreateDbContextAsync(ct);
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, ct)
            ?? throw new InvalidOperationException($"Supplier {supplierId} not found.");
        if (await db.Suppliers.AnyAsync(s => s.Id != supplierId && s.Code == code, ct))
        {
            throw new InvalidOperationException($"Supplier code '{code}' is already in use.");
        }
        supplier.Code = code;
        supplier.Name = name;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSupplierAsync(int supplierId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, ct)
            ?? throw new InvalidOperationException($"Supplier {supplierId} not found.");
        if (await db.OrderLines.AnyAsync(l => l.SupplierId == supplierId, ct)
            || await db.SupplierReports.AnyAsync(r => r.SupplierId == supplierId, ct)
            || await db.SupplierModelMaps.AnyAsync(m => m.SupplierId == supplierId, ct)
            || await db.SupplierOrders.AnyAsync(o => o.SupplierId == supplierId, ct))
        {
            throw new InvalidOperationException($"Supplier '{supplier.Code}' is referenced by orders, service reports, model maps or purchase orders.");
        }
        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync(ct);
    }

    // --- Supplier model maps: which supplier produces a given catalogue model ---

    public async Task<List<SupplierModelMap>> SupplierModelMapsAsync(int supplierId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SupplierModelMaps.AsNoTracking()
            .Where(m => m.SupplierId == supplierId)
            .OrderBy(m => m.ModelCode).ToListAsync(ct);
    }

    public async Task AddSupplierModelMapAsync(int supplierId, string modelCode, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var code = RequireTrimmed(modelCode, "Model code");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.SupplierModelMaps.AnyAsync(m => m.ModelCode == code, ct))
        {
            throw new InvalidOperationException($"Model code '{code}' is already mapped to a supplier.");
        }
        db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = supplierId, ModelCode = code });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveSupplierModelMapAsync(int mapId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.SupplierModelMaps.Where(m => m.Id == mapId).ExecuteDeleteAsync(ct);
    }

    // A null-supplier SupplierModelMap row explicitly marks a model as produced in-house rather
    // than by any supplier - PurchasingService's sweep and unresolved feed both treat it as
    // resolved-but-excluded (see PurchasingService.ResolveCandidatesAsync). Idempotent if already
    // in-house; rejects a code already mapped to a real supplier.
    public async Task MarkModelInHouseAsync(string modelCode, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var code = RequireTrimmed(modelCode, "Model code");
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.SupplierModelMaps.FirstOrDefaultAsync(m => m.ModelCode == code, ct);
        if (existing is { SupplierId: not null })
        {
            throw new InvalidOperationException($"Model code '{code}' is already mapped to a supplier.");
        }
        if (existing is null)
        {
            db.SupplierModelMaps.Add(new SupplierModelMap { SupplierId = null, ModelCode = code });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UnmarkModelInHouseAsync(string modelCode, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var code = RequireTrimmed(modelCode, "Model code");
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.SupplierModelMaps.Where(m => m.ModelCode == code && m.SupplierId == null).ExecuteDeleteAsync(ct);
    }

    // --- Addresses: seller / supplier / consumer-primary upsert in place ---

    public async Task SetSellerAddressAsync(int sellerId, Address addressValues, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var seller = await db.Sellers.Include(s => s.Address).FirstOrDefaultAsync(s => s.Id == sellerId, ct)
            ?? throw new InvalidOperationException($"Seller {sellerId} not found.");
        ApplyAddress(seller.Address is null ? seller.Address = NewAddress() : seller.Address, addressValues);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetSupplierAddressAsync(int supplierId, Address addressValues, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var supplier = await db.Suppliers.Include(s => s.Address).FirstOrDefaultAsync(s => s.Id == supplierId, ct)
            ?? throw new InvalidOperationException($"Supplier {supplierId} not found.");
        ApplyAddress(supplier.Address is null ? supplier.Address = NewAddress() : supplier.Address, addressValues);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetConsumerPrimaryAddressAsync(int consumerId, Address addressValues, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var consumer = await db.Consumers.Include(c => c.PrimaryAddress).FirstOrDefaultAsync(c => c.Id == consumerId, ct)
            ?? throw new InvalidOperationException($"Consumer {consumerId} not found.");
        ApplyAddress(consumer.PrimaryAddress is null ? consumer.PrimaryAddress = NewAddress() : consumer.PrimaryAddress, addressValues);
        await db.SaveChangesAsync(ct);
    }

    // --- Consumer delivery-address book ---

    public async Task<List<ConsumerDeliveryAddress>> DeliveryAddressesAsync(int consumerId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ConsumerDeliveryAddresses.AsNoTracking()
            .Include(d => d.Address)!.ThenInclude(a => a!.Region)
            .Where(d => d.ConsumerId == consumerId)
            .OrderBy(d => d.Label).ToListAsync(ct);
    }

    public async Task<ConsumerDeliveryAddress> AddDeliveryAddressAsync(int consumerId, string label, Address addressValues, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var trimmedLabel = (label ?? "").Trim();
        if (trimmedLabel.Length == 0) { throw new InvalidOperationException("A label is required."); }
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Consumers.AnyAsync(c => c.Id == consumerId, ct)) { throw new InvalidOperationException($"Consumer {consumerId} not found."); }
        var bookEntry = new ConsumerDeliveryAddress
        {
            ConsumerId = consumerId,
            Address = new Address { Street = "", Number = "", PostalCode = "", City = "" },
            Label = trimmedLabel,
            IsDefault = !await db.ConsumerDeliveryAddresses.AnyAsync(d => d.ConsumerId == consumerId, ct),
        };
        ApplyAddress(bookEntry.Address, addressValues);
        db.ConsumerDeliveryAddresses.Add(bookEntry);
        await db.SaveChangesAsync(ct);
        return bookEntry;
    }

    public async Task UpdateDeliveryAddressAsync(int deliveryAddressId, string label, Address addressValues, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        var trimmedLabel = RequireTrimmed(label, "A label");
        await using var db = await factory.CreateDbContextAsync(ct);
        var entry = await db.ConsumerDeliveryAddresses.Include(d => d.Address).FirstOrDefaultAsync(d => d.Id == deliveryAddressId, ct)
            ?? throw new InvalidOperationException($"Delivery address {deliveryAddressId} not found.");
        entry.Label = trimmedLabel;
        ApplyAddress(entry.Address!, addressValues);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetDefaultDeliveryAddressAsync(int deliveryAddressId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var entry = await db.ConsumerDeliveryAddresses.FirstOrDefaultAsync(d => d.Id == deliveryAddressId, ct)
            ?? throw new InvalidOperationException($"Delivery address {deliveryAddressId} not found.");
        var siblings = await db.ConsumerDeliveryAddresses
            .Where(d => d.ConsumerId == entry.ConsumerId && d.Id != entry.Id).ToListAsync(ct);
        foreach (var sibling in siblings) { sibling.IsDefault = false; }
        entry.IsDefault = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveDeliveryAddressAsync(int deliveryAddressId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var entry = await db.ConsumerDeliveryAddresses.FirstOrDefaultAsync(d => d.Id == deliveryAddressId, ct)
            ?? throw new InvalidOperationException($"Delivery address {deliveryAddressId} not found.");
        if (await db.Orders.AnyAsync(o => o.DeliveryAddressId == entry.AddressId, ct))
        {
            throw new InvalidOperationException("This address is used by an order.");
        }
        if (entry.IsDefault && await db.ConsumerDeliveryAddresses.AnyAsync(d => d.ConsumerId == entry.ConsumerId && d.Id != entry.Id, ct))
        {
            throw new InvalidOperationException("Set another default first.");
        }
        // Remove the book row only - leave the Address row behind. Nothing else FK-references
        // an orphaned book address, so this is cheap and safe; no need to re-check referrers
        // before deciding whether to also delete the Address row.
        db.ConsumerDeliveryAddresses.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ConsumerDeliveryAddress?> DefaultDeliveryAddressAsync(int consumerId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ConsumerDeliveryAddresses.AsNoTracking()
            .Include(d => d.Address)!.ThenInclude(a => a!.Region)
            .FirstOrDefaultAsync(d => d.ConsumerId == consumerId && d.IsDefault, ct);
    }

    private static string RequireTrimmed(string value, string fieldLabel)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0) { throw new InvalidOperationException($"{fieldLabel} is required."); }
        return trimmed;
    }

    private static Address NewAddress() => new() { Street = "", Number = "", PostalCode = "", City = "" };

    private static void ApplyAddress(Address target, Address source)
    {
        target.Street = source.Street.Trim();
        target.Number = source.Number.Trim();
        target.Box = string.IsNullOrWhiteSpace(source.Box) ? null : source.Box.Trim();
        target.PostalCode = source.PostalCode.Trim();
        target.City = source.City.Trim();
        target.CountryCode = string.IsNullOrWhiteSpace(source.CountryCode) ? "BE" : source.CountryCode.Trim();
        target.RegionId = source.RegionId;
    }

    private async Task RequireAdminOrOfficeAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office)) { return; }
        throw new InvalidOperationException("Only Admin or Office can do this.");
    }
}
