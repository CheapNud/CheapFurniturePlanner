using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

// Firms are our own legal entities (a firm = one accounting ledger); collections attach
// catalogue collection codes to firms. Mutations are Admin-only - this is the company's
// legal identity, the same trust level as /users. Exactly one firm is the default: the first
// created firm becomes it automatically and SetDefaultAsync moves the flag atomically.
public sealed class FirmService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser)
{
    public async Task<List<Firm>> FirmsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Firms.AsNoTracking()
            .Include(f => f.Address)!.ThenInclude(a => a!.Region)
            .OrderBy(f => f.Name).ToListAsync(ct);
    }

    public async Task<List<Collection>> AllCollectionsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Collections.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct);
    }

    public async Task<Firm> AddFirmAsync(Firm firmValues, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        var code = RequireTrimmed(firmValues.Code, "Firm code");
        var name = RequireTrimmed(firmValues.Name, "Firm name");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Firms.AnyAsync(f => f.Code == code, ct))
        {
            throw new InvalidOperationException($"Firm code '{code}' is already in use.");
        }
        var firm = new Firm { Code = code, Name = name, IsDefault = !await db.Firms.AnyAsync(ct) };
        ApplyValues(firm, firmValues);
        db.Firms.Add(firm);
        await db.SaveChangesAsync(ct);
        return firm;
    }

    public async Task UpdateFirmAsync(int firmId, Firm firmValues, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        var code = RequireTrimmed(firmValues.Code, "Firm code");
        var name = RequireTrimmed(firmValues.Name, "Firm name");
        await using var db = await factory.CreateDbContextAsync(ct);
        var firm = await db.Firms.Include(f => f.Address).FirstOrDefaultAsync(f => f.Id == firmId, ct)
            ?? throw new InvalidOperationException($"Firm {firmId} not found.");
        if (await db.Firms.AnyAsync(f => f.Id != firmId && f.Code == code, ct))
        {
            throw new InvalidOperationException($"Firm code '{code}' is already in use.");
        }
        firm.Code = code;
        firm.Name = name;
        ApplyValues(firm, firmValues);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetDefaultAsync(int firmId, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var firm = await db.Firms.FirstOrDefaultAsync(f => f.Id == firmId, ct)
            ?? throw new InvalidOperationException($"Firm {firmId} not found.");
        foreach (var other in await db.Firms.Where(f => f.Id != firmId && f.IsDefault).ToListAsync(ct))
        {
            other.IsDefault = false;
        }
        firm.IsDefault = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteFirmAsync(int firmId, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var firm = await db.Firms.FirstOrDefaultAsync(f => f.Id == firmId, ct)
            ?? throw new InvalidOperationException($"Firm {firmId} not found.");
        if (await db.Collections.AnyAsync(c => c.FirmId == firmId, ct))
        {
            throw new InvalidOperationException("Cannot delete a firm with collections.");
        }
        if (await db.Orders.AnyAsync(o => o.FirmId == firmId, ct))
        {
            throw new InvalidOperationException("Cannot delete a firm with orders.");
        }
        if (firm.IsDefault && await db.Firms.AnyAsync(f => f.Id != firmId, ct))
        {
            throw new InvalidOperationException("Make another firm the default first.");
        }
        db.Firms.Remove(firm);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Collection> AddCollectionAsync(int firmId, string collectionCode, string collectionName, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        var code = RequireTrimmed(collectionCode, "Collection code");
        var name = RequireTrimmed(collectionName, "Collection name");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Firms.AnyAsync(f => f.Id == firmId, ct))
        {
            throw new InvalidOperationException($"Firm {firmId} not found.");
        }
        if (await db.Collections.AnyAsync(c => c.Code == code, ct))
        {
            throw new InvalidOperationException($"Collection code '{code}' is already in use.");
        }
        var registryRow = new Collection { Code = code, Name = name, FirmId = firmId };
        db.Collections.Add(registryRow);
        await db.SaveChangesAsync(ct);
        return registryRow;
    }

    public async Task RenameCollectionAsync(int collectionId, string collectionName, CancellationToken ct = default)
    {
        await RequireAdminAsync();
        var name = RequireTrimmed(collectionName, "Collection name");
        await using var db = await factory.CreateDbContextAsync(ct);
        var registryRow = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId, ct)
            ?? throw new InvalidOperationException($"Collection {collectionId} not found.");
        registryRow.Name = name;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteCollectionAsync(int collectionId, CancellationToken ct = default)
    {
        // Always allowed: the catalogue soft-links by code, so affected models simply fall
        // back to the default firm.
        await RequireAdminAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Collections.Where(c => c.Id == collectionId).ExecuteDeleteAsync(ct);
    }

    // Scalar + address application shared by add/update. The address is created on first use
    // and edited in place afterwards (the PartyService ApplyAddress idiom).
    private static void ApplyValues(Firm firm, Firm firmValues)
    {
        firm.VatNumber = Trimmed(firmValues.VatNumber);
        firm.Iban = Trimmed(firmValues.Iban);
        firm.Bic = Trimmed(firmValues.Bic);
        firm.Email = Trimmed(firmValues.Email);
        firm.Phone = Trimmed(firmValues.Phone);
        firm.PeppolEndpointId = Trimmed(firmValues.PeppolEndpointId);
        if (firmValues.Address is not null)
        {
            firm.Address ??= new Address { Street = "", Number = "", PostalCode = "", City = "" };
            firm.Address.Street = firmValues.Address.Street;
            firm.Address.Number = firmValues.Address.Number;
            firm.Address.Box = firmValues.Address.Box;
            firm.Address.PostalCode = firmValues.Address.PostalCode;
            firm.Address.City = firmValues.Address.City;
            firm.Address.CountryCode = firmValues.Address.CountryCode;
            firm.Address.RegionId = firmValues.Address.RegionId;
        }
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequireTrimmed(string value, string fieldLabel)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0) { throw new InvalidOperationException($"{fieldLabel} is required."); }
        return trimmed;
    }

    private async Task RequireAdminAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin)) { return; }
        throw new InvalidOperationException("Only Admin can manage firms.");
    }
}
