using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 2 of SP-3: MaterialPlanningService covers MaterialProfile CRUD and the MaterialSupplierTerm
// preferred-term invariant. Harness mirrors PartyServiceTests - in-memory SQLite, migrated schema.
public class MaterialPlanningServiceTests
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

    private static async Task<(int SupplierAId, int SupplierBId)> SeedSuppliersAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var supplierA = new Supplier { Code = "SUP-A", Name = "Supplier A" };
        var supplierB = new Supplier { Code = "SUP-B", Name = "Supplier B" };
        db.Suppliers.AddRange(supplierA, supplierB);
        await db.SaveChangesAsync();
        return (supplierA.Id, supplierB.Id);
    }

    // --- Profiles ---

    [Fact]
    public async Task UpsertProfile_InsertsThenUpdatesInPlace_ByIdentity()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));

        await service.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Foam, Code = " F-20 ", MinimumStock = 5m });
        var afterInsert = Assert.Single(await service.ProfilesAsync());
        Assert.Equal("F-20", afterInsert.Code);
        Assert.Equal(5m, afterInsert.MinimumStock);
        Assert.Null(afterInsert.AverageUsageOverride);

        // Same identity (trimmed code matches) - upsert edits in place, no second row.
        await service.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Foam, Code = "F-20", MinimumStock = 8m, AverageUsageOverride = 2.5m });
        var profiles = await service.ProfilesAsync();
        var updated = Assert.Single(profiles);
        Assert.Equal(afterInsert.Id, updated.Id);
        Assert.Equal(8m, updated.MinimumStock);
        Assert.Equal(2.5m, updated.AverageUsageOverride);
    }

    [Fact]
    public async Task UpsertProfile_RejectsInvalidValues()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Foam, Code = "F-20", MinimumStock = -1m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Foam, Code = "F-20", AverageUsageOverride = 0m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Foam, Code = "   " }));
    }

    [Fact]
    public async Task DeleteProfile_RemovesRow()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var profile = await service.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Foam, Code = "F-20", MinimumStock = 5m });

        await service.DeleteProfileAsync(profile.Id);

        Assert.Empty(await service.ProfilesAsync());
    }

    [Fact]
    public async Task ProfileMutations_RejectNonAdminOrOffice()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("mechanic-1", Roles.Mechanic));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertProfileAsync(new MaterialProfile { Kind = MaterialKind.Foam, Code = "F-20", MinimumStock = 5m }));
    }

    // --- Supplier terms: the preferred-term invariant matrix ---

    [Fact]
    public async Task UpsertTerm_FirstForMaterial_IsAutoPreferred()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, _) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));

        var term = await service.UpsertTermAsync(new MaterialSupplierTerm
        {
            Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 3,
        });

        Assert.True(term.IsPreferred);
    }

    [Fact]
    public async Task UpsertTerm_SecondForMaterial_IsNotPreferred_AndDoesNotDisturbFirst()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, supplierBId) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));

        var first = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 3 });
        var second = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierBId, DeliveryTimeDays = 5 });

        Assert.False(second.IsPreferred);
        var terms = await service.TermsAsync(MaterialKind.Foam, "F-20", null);
        Assert.True(terms.Single(t => t.Id == first.Id).IsPreferred);
    }

    [Fact]
    public async Task UpsertTerm_SameIdentity_UpdatesInPlace_WithoutTouchingPreferredFlag()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, _) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var term = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 3 });

        var updated = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 7, UnitPrice = 1.5m });

        Assert.Equal(term.Id, updated.Id);
        Assert.Equal(7, updated.DeliveryTimeDays);
        Assert.Equal(1.5m, updated.UnitPrice);
        Assert.True(updated.IsPreferred);
    }

    [Fact]
    public async Task SetPreferred_SwapsAtomically_WithinMaterialIdentity_Only()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, supplierBId) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var first = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 3 });
        var second = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierBId, DeliveryTimeDays = 5 });
        // A term for a different material identity, also preferred - must be left untouched by the swap.
        var otherMaterial = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Frame, Code = "FR-1", SupplierId = supplierAId, DeliveryTimeDays = 2 });

        await service.SetPreferredAsync(second.Id);

        var terms = await service.TermsAsync(MaterialKind.Foam, "F-20", null);
        Assert.False(terms.Single(t => t.Id == first.Id).IsPreferred);
        Assert.True(terms.Single(t => t.Id == second.Id).IsPreferred);
        Assert.True((await service.TermsAsync(MaterialKind.Frame, "FR-1", null)).Single(t => t.Id == otherMaterial.Id).IsPreferred);
    }

    [Fact]
    public async Task UpsertTerm_RejectsInvalidValues()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, _) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = -1 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, MinimumOrderQuantity = 0m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, UnitsPerPackage = -2m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, UnitPrice = 0m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = 9999 }));
    }

    [Fact]
    public async Task TermMutations_RejectNonAdminOrOffice()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, _) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("mechanic-1", Roles.Mechanic));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId }));
    }

    [Fact]
    public async Task DeleteTerm_Preferred_WithSiblings_IsGuarded()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, supplierBId) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var first = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 3 });
        await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierBId, DeliveryTimeDays = 5 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteTermAsync(first.Id));
    }

    [Fact]
    public async Task DeleteTerm_NonPreferred_Succeeds()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, supplierBId) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));
        await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 3 });
        var second = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierBId, DeliveryTimeDays = 5 });

        await service.DeleteTermAsync(second.Id);

        var terms = await service.TermsAsync(MaterialKind.Foam, "F-20", null);
        Assert.Single(terms);
    }

    [Fact]
    public async Task DeleteTerm_LastRemaining_IsAllowed()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, _) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var only = await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 3 });
        Assert.True(only.IsPreferred);

        await service.DeleteTermAsync(only.Id);

        Assert.Empty(await service.TermsAsync(MaterialKind.Foam, "F-20", null));
    }

    [Fact]
    public async Task AllTermsAsync_ReturnsEveryMaterialIdentity()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (supplierAId, supplierBId) = await SeedSuppliersAsync(factory);
        var service = new MaterialPlanningService(factory, new FakeCurrentUser("office-1", Roles.Office));
        await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Foam, Code = "F-20", SupplierId = supplierAId, DeliveryTimeDays = 3 });
        await service.UpsertTermAsync(new MaterialSupplierTerm { Kind = MaterialKind.Frame, Code = "FR-1", SupplierId = supplierBId, DeliveryTimeDays = 2 });

        var all = await service.AllTermsAsync();

        Assert.Equal(2, all.Count);
    }
}
