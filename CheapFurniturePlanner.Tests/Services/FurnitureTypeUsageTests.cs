using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Regression net for the usage-statistics crash the desktop smoke run surfaced:
// PlannerFurnitureItem.FurnitureItemId is nullable by design (planner items without a catalogue
// backing), but GetFurnitureTypeUsageAsync grouped by FurnitureItem.Type unconditionally - the
// LEFT JOIN yields a NULL type for unbacked rows and materializing the non-nullable enum key
// throws "Nullable object must have a value".
public class FurnitureTypeUsageTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task TypeUsage_IgnoresPlannerItemsWithoutCatalogueBacking()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var _ = connection;
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        await using (var migrateContext = new FurniturePlannerContext(options))
        {
            await migrateContext.Database.MigrateAsync();
            await RoleSeeder.SeedAsync(migrateContext);
        }

        var factory = new TestDbContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var plan = new RoomPlan { Name = "Smoke plan", Width = 400, Height = 300 };
            db.RoomPlans.Add(plan);
            await db.SaveChangesAsync();
            // FurnitureItem Id 1 exists from the seeded catalogue (a Sofa); one backed row, one
            // unbacked row - the unbacked one used to blow up the whole statistics query.
            db.PlannerFurnitureItems.Add(new PlannerFurnitureItem { RoomPlanId = plan.Id, FurnitureItemId = 1, UIId = 1, X = 0, Y = 0 });
            db.PlannerFurnitureItems.Add(new PlannerFurnitureItem { RoomPlanId = plan.Id, FurnitureItemId = null, UIId = 2, X = 10, Y = 10 });
            await db.SaveChangesAsync();
        }

        var repository = new FurniturePlannerRepository(factory);
        var usage = await repository.GetFurnitureTypeUsageAsync();

        var entry = Assert.Single(usage);
        Assert.Equal(FurnitureType.Sofa, entry.Key);
        Assert.Equal(1, entry.Value);
    }
}
