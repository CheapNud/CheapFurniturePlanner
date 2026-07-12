using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Tests.Fixtures;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Task 1: MasterAuthoringService edits the seven flat pricing masters in the working masters
// document (load full snapshot -> mutate the master list -> SaveMastersAsync), never republishing.
// Codes are immutable (update matches by key). Harness mirrors PriceVersionServiceTests: in-memory
// SQLite, store seeded from the embedded seed.
public class MasterAuthoringServiceTests
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

    private static async Task<(MasterAuthoringService Service, AuthoringCatalogueStore Store)> NewAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        return (new MasterAuthoringService(store), store);
    }

    [Fact]
    public async Task AddMaterial_PersistsThroughStore()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);

        await service.AddMaterialAsync(new Material("MAT-NEW", "New material", 4.5m, "m"));

        var masters = await store.LoadAsync();
        Assert.Contains(masters.Materials, m => m.Code == "MAT-NEW" && m.UnitCost == 4.5m);
    }

    [Fact]
    public async Task AddMaterial_DuplicateCode_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);
        var existing = (await store.LoadAsync()).Materials[0].Code;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddMaterialAsync(new Material(existing, "dup", 1m, "m")));
    }

    [Fact]
    public async Task AddMaterial_NegativeUnitCost_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, _) = await NewAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddMaterialAsync(new Material("MAT-NEG", "neg", -1m, "m")));
    }

    [Fact]
    public async Task AddMaterial_EmptyCode_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, _) = await NewAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddMaterialAsync(new Material("   ", "x", 1m, "m")));
    }

    [Fact]
    public async Task AddMaterial_TrimmedDuplicate_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, _) = await NewAsync(factory);
        await service.AddMaterialAsync(new Material("MAT-DUP", "first", 1m, "m"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddMaterialAsync(new Material("MAT-DUP ", "second", 1m, "m")));
    }

    [Fact]
    public async Task UpdateMaterial_EditsScalars_KeepsCode()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);
        var code = (await store.LoadAsync()).Materials[0].Code;

        // Even if a caller passes a different Code, the key argument pins identity (immutable code).
        await service.UpdateMaterialAsync(code, new Material("IGNORED", "Renamed", 9.99m, "kg"));

        var masters = await store.LoadAsync();
        Assert.DoesNotContain(masters.Materials, m => m.Code == "IGNORED");
        var updated = masters.Materials.Single(m => m.Code == code);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(9.99m, updated.UnitCost);
    }

    [Fact]
    public async Task DeleteMaterial_RemovesRow()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);
        await service.AddMaterialAsync(new Material("MAT-TMP", "tmp", 1m, "m"));

        await service.DeleteMaterialAsync("MAT-TMP");

        Assert.DoesNotContain((await store.LoadAsync()).Materials, m => m.Code == "MAT-TMP");
    }

    [Fact]
    public async Task AddSprayPrice_UnknownFrameBody_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, _) = await NewAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddSprayPriceAsync(new SprayPrice("FRAME-DOES-NOT-EXIST", 3m)));
    }

    [Fact]
    public async Task UpdatePriceGroup_PreservesId()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);
        var original = (await store.LoadAsync()).PriceGroups[0];

        await service.UpdatePriceGroupAsync(original.Code, new PriceGroup
        {
            Code = "IGNORED",
            Kind = original.Kind,
            RatePerMeter = original.RatePerMeter + 5m,
            MaterialTypeCode = original.MaterialTypeCode
        });

        var updated = (await store.LoadAsync()).PriceGroups.Single(p => p.Code == original.Code);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.RatePerMeter + 5m, updated.RatePerMeter);
    }

    [Fact]
    public async Task ServiceEdit_DoesNotChangeGoldenEngineOutput()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, _) = await NewAsync(factory);

        // The pricing engine reads the DemoWorld fixture, never the DB authoring store. Editing a
        // working master must not leak into engine output — this pins the golden-neutral invariant.
        var before = CanonicalJson.Serialize(DemoWorld.Load());
        await service.AddMaterialAsync(new Material("MAT-LEAK-CHECK", "x", 1m, "m"));
        var after = CanonicalJson.Serialize(DemoWorld.Load());
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task AddFabricGroup_PersistsWithColors()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);
        var priceGroup = (await store.LoadAsync()).PriceGroups[0].Code;

        await service.AddFabricGroupAsync(new FabricGroup
        {
            Code = "FG-NEW",
            PriceGroupCode = priceGroup,
            Colors = [new FabricColor { Code = "COL-A", Name = "Sand", PurchasePrice = 3m, ShippingCost = 1m }]
        });

        var group = (await store.LoadAsync()).FabricGroups.Single(g => g.Code == "FG-NEW");
        Assert.Equal(priceGroup, group.PriceGroupCode);
        Assert.Single(group.Colors);
    }

    [Fact]
    public async Task AddFabricGroup_UnknownPriceGroup_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, _) = await NewAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFabricGroupAsync(new FabricGroup
        {
            Code = "FG-BAD", PriceGroupCode = "PG-DOES-NOT-EXIST", Colors = []
        }));
    }

    [Fact]
    public async Task AddFabricGroup_DuplicateColorCode_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);
        var priceGroup = (await store.LoadAsync()).PriceGroups[0].Code;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFabricGroupAsync(new FabricGroup
        {
            Code = "FG-DUP", PriceGroupCode = priceGroup,
            Colors =
            [
                new FabricColor { Code = "COL-X", Name = "a", PurchasePrice = 1m, ShippingCost = 0m },
                new FabricColor { Code = "COL-X", Name = "b", PurchasePrice = 1m, ShippingCost = 0m }
            ]
        }));
    }

    [Fact]
    public async Task UpdateFabricGroup_PreservesId_ReplacesColors()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);
        var priceGroup = (await store.LoadAsync()).PriceGroups[0].Code;
        await service.AddFabricGroupAsync(new FabricGroup { Code = "FG-UPD", PriceGroupCode = priceGroup, Colors = [] });
        var originalId = (await store.LoadAsync()).FabricGroups.Single(g => g.Code == "FG-UPD").Id;

        await service.UpdateFabricGroupAsync("FG-UPD", new FabricGroup
        {
            Code = "IGNORED", PriceGroupCode = priceGroup,
            Colors = [new FabricColor { Code = "COL-B", Name = "Ash", PurchasePrice = 2m, ShippingCost = 0m }]
        });

        var group = (await store.LoadAsync()).FabricGroups.Single(g => g.Code == "FG-UPD");
        Assert.Equal(originalId, group.Id);
        Assert.Single(group.Colors);
        Assert.DoesNotContain((await store.LoadAsync()).FabricGroups, g => g.Code == "IGNORED");
    }

    [Fact]
    public async Task AddCombinationPriceRule_NegativeAdjustment_Accepted()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);

        await service.AddCombinationPriceRuleAsync(new CombinationPriceRule(
            [new SelectionKey("OPT-A", "CH-1")], -15m));

        Assert.Contains((await store.LoadAsync()).CombinationPriceRules, r => r.Adjustment == -15m);
    }

    [Fact]
    public async Task AddCombinationPriceRule_EmptySelections_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, _) = await NewAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddCombinationPriceRuleAsync(new CombinationPriceRule([], 5m)));
    }

    [Fact]
    public async Task AddCombinationPriceRule_DuplicateOption_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, _) = await NewAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddCombinationPriceRuleAsync(new CombinationPriceRule(
                [new SelectionKey("OPT-A", "CH-1"), new SelectionKey("OPT-A", "CH-2")], 5m)));
    }

    [Fact]
    public async Task UpdateAndRemoveCombinationPriceRule_ByIndex()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);
        await service.AddCombinationPriceRuleAsync(new CombinationPriceRule([new SelectionKey("OPT-A", "CH-1")], 5m));
        var index = (await store.LoadAsync()).CombinationPriceRules.Count - 1;

        await service.UpdateCombinationPriceRuleAsync(index, new CombinationPriceRule([new SelectionKey("OPT-B", "CH-9")], 8m));
        Assert.Equal(8m, (await store.LoadAsync()).CombinationPriceRules[index].Adjustment);

        await service.RemoveCombinationPriceRuleAsync(index);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateCombinationPriceRuleAsync(index, new CombinationPriceRule([new SelectionKey("OPT-B", "CH-9")], 1m)));
    }

    [Fact]
    public async Task AddMarket_PersistsAndRejectsDuplicate()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);

        await service.AddMarketAsync(new MarketParameters("MKT-NEW", 1m, 2m, [], new RoundingPolicy(2, 2, MidpointRounding.ToEven, RoundStage.None)));
        Assert.Contains((await store.LoadAsync()).Markets, m => m.Code == "MKT-NEW");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddMarketAsync(new MarketParameters("MKT-NEW", 1m, 2m, [], new RoundingPolicy(2, 2, MidpointRounding.ToEven, RoundStage.None))));
    }

    [Fact]
    public async Task AddMarket_NegativeRate_Throws()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, _) = await NewAsync(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddMarketAsync(new MarketParameters("MKT-NEG", -1m, 0m, [], new RoundingPolicy(2, 2, MidpointRounding.ToEven, RoundStage.None))));
    }

    [Fact]
    public async Task DeleteMarket_NonLastSucceeds_LastThrows()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var (service, store) = await NewAsync(factory);
        await service.AddMarketAsync(new MarketParameters("MKT-EXTRA", 0m, 0m, [], new RoundingPolicy(2, 2, MidpointRounding.ToEven, RoundStage.None)));

        // Deleting a non-last market succeeds.
        await service.DeleteMarketAsync("MKT-EXTRA");

        // Delete down to the final remaining market; deleting that last one is blocked.
        var remaining = (await store.LoadAsync()).Markets.Select(m => m.Code).ToList();
        for (var i = 0; i < remaining.Count - 1; i++) { await service.DeleteMarketAsync(remaining[i]); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteMarketAsync(remaining[^1]));
        Assert.Single((await store.LoadAsync()).Markets);
    }
}
