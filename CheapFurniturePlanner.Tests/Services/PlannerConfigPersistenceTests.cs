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

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
