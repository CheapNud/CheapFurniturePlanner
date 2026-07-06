using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Mappings;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Repositories;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.ViewModels;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

public class PlannerConfigPersistenceTests
{
    // Mirrors SchemaTests.NewContext(): the connection is not owned by any single context, so callers
    // must dispose it themselves to keep the in-memory database alive for the test's duration.
    private static (DbContextOptions<FurniturePlannerContext> Options, Microsoft.Data.Sqlite.SqliteConnection Connection) NewOptions()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (options, conn);
    }

    private static PlannerService NewPlannerService(DbContextOptions<FurniturePlannerContext> options)
    {
        var config = new TypeAdapterConfig();
        FurniturePlannerMappingProfile.Configure(config);
        IMapper mapper = new Mapper(config);
        var repository = new FurniturePlannerRepository(new TestDbContextFactory(options));
        return new PlannerService(repository, mapper, NullLogger<PlannerService>.Instance);
    }

    private static RoomPlanService NewRoomPlanService(DbContextOptions<FurniturePlannerContext> options)
    {
        var config = new TypeAdapterConfig();
        FurniturePlannerMappingProfile.Configure(config);
        IMapper mapper = new Mapper(config);
        var repository = new FurniturePlannerRepository(new TestDbContextFactory(options));
        return new RoomPlanService(repository, mapper, NullLogger<RoomPlanService>.Instance);
    }

    [Fact]
    public async Task AddFurnitureToRoomAsync_ElementPlacement_RoundTripsConfigurationAndLeavesFurnitureItemIdNull()
    {
        var (options, conn) = NewOptions();
        using var _ = conn;

        int roomId;
        using (var seedContext = new FurniturePlannerContext(options))
        {
            var room = new RoomPlan { Name = "Living Room", Width = 400, Height = 300 };
            seedContext.RoomPlans.Add(room);
            await seedContext.SaveChangesAsync();
            roomId = room.Id;
        }

        var service = NewPlannerService(options);

        var placement = new FurniturePlannerViewModel
        {
            UIId = 1,
            FurnitureItemId = null,
            ElementCode = "FJ2",
            CatalogueVersion = "1",
            FabricColorCode = "AQUA-BLUE",
            Selections = new Dictionary<string, string> { ["DEPTH"] = "STD" }
        };

        await service.AddFurnitureToRoomAsync(roomId, placement);

        using var readContext = new FurniturePlannerContext(options);
        var saved = readContext.PlannerFurnitureItems.Single(p => p.RoomPlanId == roomId);

        Assert.Null(saved.FurnitureItemId);
        Assert.Equal("FJ2", saved.ElementCode);
        Assert.Equal("1", saved.CatalogueVersion);
        Assert.Equal("AQUA-BLUE", saved.FabricColorCode);
        Assert.NotNull(saved.SelectionsJson);

        var roundTripped = PlannerService.DeserializeSelections(saved.SelectionsJson);
        Assert.Equal("STD", roundTripped["DEPTH"]);
    }

    [Fact]
    public async Task GetRoomPlanWithFurnitureAsync_RoundTripsConfigurationAndSurvivesSubsequentSave()
    {
        var (options, conn) = NewOptions();
        using var _ = conn;

        int roomId;
        int flatFurnitureItemId;
        using (var seedContext = new FurniturePlannerContext(options))
        {
            var room = new RoomPlan { Name = "Living Room", Width = 400, Height = 300 };
            seedContext.RoomPlans.Add(room);

            var flatItem = new FurnitureItem
            {
                Code = "SOFA-1",
                Name = "Classic Sofa",
                Type = FurnitureType.Sofa,
                Width = 200,
                Length = 90,
                Height = 80
            };
            seedContext.FurnitureItems.Add(flatItem);

            await seedContext.SaveChangesAsync();
            roomId = room.Id;
            flatFurnitureItemId = flatItem.Id;
        }

        var plannerService = NewPlannerService(options);
        var roomPlanService = NewRoomPlanService(options);

        // Configured element placement (Phase 2B) - no flat catalog link.
        var elementPlacement = new FurniturePlannerViewModel
        {
            UIId = 1,
            FurnitureItemId = null,
            ElementCode = "FJ2",
            CatalogueVersion = "1",
            FabricColorCode = "AQUA-BLUE",
            CachedVariantCode = "FJ2-AQUA-BLUE-STD",
            CachedUnitPrice = 249.99m,
            Selections = new Dictionary<string, string> { ["DEPTH"] = "STD" }
        };
        await plannerService.AddFurnitureToRoomAsync(roomId, elementPlacement);

        // Legacy flat-catalog placement - still expected to load correctly.
        var legacyPlacement = new FurniturePlannerViewModel
        {
            UIId = 2,
            FurnitureItemId = flatFurnitureItemId
        };
        await plannerService.AddFurnitureToRoomAsync(roomId, legacyPlacement);

        // --- Load via the actual load path used by the UI ---
        var loaded = await roomPlanService.GetRoomPlanWithFurnitureAsync(roomId);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.FurnitureItems.Count);

        var loadedElement = loaded.FurnitureItems.Single(f => f.UIId == 1);
        Assert.Equal("FJ2", loadedElement.ElementCode);
        Assert.Equal("1", loadedElement.CatalogueVersion);
        Assert.Equal("AQUA-BLUE", loadedElement.FabricColorCode);
        Assert.Equal("FJ2-AQUA-BLUE-STD", loadedElement.CachedVariantCode);
        Assert.Equal(249.99m, loadedElement.CachedUnitPrice);
        Assert.True(loadedElement.Selections.TryGetValue("DEPTH", out var depth));
        Assert.Equal("STD", depth);

        var loadedLegacy = loaded.FurnitureItems.Single(f => f.UIId == 2);
        Assert.Equal(flatFurnitureItemId, loadedLegacy.FurnitureItemId);
        Assert.Equal("SOFA-1", loadedLegacy.Code);
        Assert.Equal("Classic Sofa", loadedLegacy.Name);

        // --- Simulate the clobber path: pass the loaded view models straight back through
        // the autosave routine, exactly as the UI does on every drag/autosave tick. Before
        // the fix, the loaded config was empty, so this would silently wipe the DB config.
        await plannerService.SaveRoomPlanStateAsync(roomId, loaded.FurnitureItems);

        var reloaded = await roomPlanService.GetRoomPlanWithFurnitureAsync(roomId);
        Assert.NotNull(reloaded);

        var reloadedElement = reloaded!.FurnitureItems.Single(f => f.UIId == 1);
        Assert.Equal("FJ2", reloadedElement.ElementCode);
        Assert.True(reloadedElement.Selections.TryGetValue("DEPTH", out var depthAfterSave));
        Assert.Equal("STD", depthAfterSave);
        Assert.Equal("AQUA-BLUE", reloadedElement.FabricColorCode);
        Assert.Equal("FJ2-AQUA-BLUE-STD", reloadedElement.CachedVariantCode);
        Assert.Equal(249.99m, reloadedElement.CachedUnitPrice);
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
