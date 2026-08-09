using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 1: FirmService manages our own legal entities (ledgers) and the catalogue collection
// registry. Harness mirrors PartyServiceTests: in-memory SQLite, migrated schema.
public class FirmServiceTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (new TestDbContextFactory(options), connection);
    }

    private static Firm NewFirm(string code, string name) => new() { Code = code, Name = name };

    [Fact]
    public async Task FirstFirmBecomesDefault_SecondDoesNot()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new FirmService(factory, new FakeCurrentUser("admin-1", Roles.Admin));

        var first = await service.AddFirmAsync(NewFirm("ALP", "Alpine Living"));
        var second = await service.AddFirmAsync(NewFirm("URB", "Urban Nest"));

        var firms = await service.FirmsAsync();
        Assert.True(firms.Single(f => f.Id == first.Id).IsDefault);
        Assert.False(firms.Single(f => f.Id == second.Id).IsDefault);
    }

    [Fact]
    public async Task SetDefault_MovesTheFlagAtomically()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new FirmService(factory, new FakeCurrentUser("admin-1", Roles.Admin));

        var first = await service.AddFirmAsync(NewFirm("ALP", "Alpine Living"));
        var second = await service.AddFirmAsync(NewFirm("URB", "Urban Nest"));

        await service.SetDefaultAsync(second.Id);

        var firms = await service.FirmsAsync();
        var defaultFirm = Assert.Single(firms, f => f.IsDefault);
        Assert.Equal(second.Id, defaultFirm.Id);
    }

    [Fact]
    public async Task AddFirm_RejectsBlankAndDuplicateCode()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new FirmService(factory, new FakeCurrentUser("admin-1", Roles.Admin));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFirmAsync(NewFirm("   ", "Blank Code")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFirmAsync(NewFirm("BLK", "   ")));

        await service.AddFirmAsync(NewFirm("ALP", "Alpine Living"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFirmAsync(NewFirm("ALP", "Alpine Living Again")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFirmAsync(NewFirm("  ALP  ", "Alpine Living Again")));
    }

    [Fact]
    public async Task UpdateFirm_AppliesScalarsAndAddress()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new FirmService(factory, new FakeCurrentUser("admin-1", Roles.Admin));
        var firm = await service.AddFirmAsync(NewFirm("ALP", "Alpine Living"));

        var updateValues = new Firm
        {
            Code = "ALP",
            Name = "Alpine Living Renamed",
            VatNumber = "BE0999999999",
            Iban = "BE68539007547034",
            Bic = "GKCCBEBB",
            Address = new Address { Street = "Maple Row", Number = "12", PostalCode = "9990", City = "Fairbrook" },
        };
        await service.UpdateFirmAsync(firm.Id, updateValues);

        var firms = await service.FirmsAsync();
        var updated = Assert.Single(firms);
        Assert.Equal("Alpine Living Renamed", updated.Name);
        Assert.Equal("BE0999999999", updated.VatNumber);
        Assert.Equal("BE68539007547034", updated.Iban);
        Assert.Equal("GKCCBEBB", updated.Bic);
        Assert.NotNull(updated.Address);
        Assert.Equal("Fairbrook", updated.Address!.City);
    }

    [Fact]
    public async Task DeleteFirm_GuardsReferencesAndDefault()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new FirmService(factory, new FakeCurrentUser("admin-1", Roles.Admin));

        var first = await service.AddFirmAsync(NewFirm("ALP", "Alpine Living"));
        var second = await service.AddFirmAsync(NewFirm("URB", "Urban Nest"));
        await service.AddCollectionAsync(second.Id, "URB-COL", "Urban Collection");

        // 1) firm with a collection -> delete throws "collections"
        var withCollectionEx = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteFirmAsync(second.Id));
        Assert.Contains("collections", withCollectionEx.Message, StringComparison.OrdinalIgnoreCase);

        // 2) default firm while another exists -> delete throws "default"
        var defaultFirmEx = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteFirmAsync(first.Id));
        Assert.Contains("default", defaultFirmEx.Message, StringComparison.OrdinalIgnoreCase);

        // 3) sole remaining firm (no refs) -> delete succeeds, FirmsAsync empty
        await service.DeleteCollectionAsync((await service.AllCollectionsAsync()).Single().Id);
        await service.DeleteFirmAsync(second.Id);
        await service.SetDefaultAsync(first.Id);
        await service.DeleteFirmAsync(first.Id);

        Assert.Empty(await service.FirmsAsync());
    }

    [Fact]
    public async Task Collections_CrudAndUniqueCode()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new FirmService(factory, new FakeCurrentUser("admin-1", Roles.Admin));
        var firm = await service.AddFirmAsync(NewFirm("ALP", "Alpine Living"));

        await service.AddCollectionAsync(firm.Id, "ZULU", "Zulu Collection");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddCollectionAsync(firm.Id, "ZULU", "Duplicate"));

        var alpha = await service.AddCollectionAsync(firm.Id, "ALPHA", "Alpha Collection");
        await service.RenameCollectionAsync(alpha.Id, "Alpha Renamed");

        var all = await service.AllCollectionsAsync();
        Assert.Equal(["ALPHA", "ZULU"], all.Select(c => c.Code).ToList());
        Assert.Equal("Alpha Renamed", all.Single(c => c.Code == "ALPHA").Name);

        await service.DeleteCollectionAsync(alpha.Id);
        await service.DeleteCollectionAsync((await service.AllCollectionsAsync()).Single().Id);
        Assert.Empty(await service.AllCollectionsAsync());
    }

    [Fact]
    public async Task Mutations_RequireAdmin()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new FirmService(factory, new FakeCurrentUser("office-1", Roles.Office));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFirmAsync(NewFirm("ALP", "Alpine Living")));

        Assert.Empty(await service.FirmsAsync());
    }
}
