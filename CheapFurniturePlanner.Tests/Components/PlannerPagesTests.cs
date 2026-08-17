using Bunit;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Mappings;
using CheapFurniturePlanner.Repositories;
using CheapFurniturePlanner.Services;
using Mapster;
using MapsterMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// UX-2 planner-pages rider: RoomPlans (/room-plans, /room-plans/new) and FurnitureCatalog
// (/furniture/catalog, /furniture/add) had no bunit coverage before this sweep. Harness mirrors
// UsersPageTests/PlannerPagePanelTests' lightest-needed wiring: real repository + Mapster mapper
// against an in-memory SQLite DB, only the services each page actually injects.
public class PlannerPagesTests : TestContext
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
            // FurniturePlannerContext seeds one demo RoomPlan + five FurnitureItems via HasData -
            // strip them so "empty store" tests exercise a genuinely empty list, not the demo data.
            migrateContext.RoomPlans.RemoveRange(migrateContext.RoomPlans);
            migrateContext.FurnitureItems.RemoveRange(migrateContext.FurnitureItems);
            migrateContext.SaveChanges();
        }
        return (new TestDbContextFactory(options), connection);
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var mapsterConfig = new TypeAdapterConfig();
        FurniturePlannerMappingProfile.Configure(mapsterConfig);
        IMapper mapper = new Mapper(mapsterConfig);
        var repository = new FurniturePlannerRepository(factory);

        Services.AddMudServices();
        Services.AddSingleton(repository);
        Services.AddSingleton(mapper);
        Services.AddSingleton(sp => new RoomPlanService(sp.GetRequiredService<FurniturePlannerRepository>(), sp.GetRequiredService<IMapper>(), NullLogger<RoomPlanService>.Instance));
        Services.AddSingleton(sp => new PlannerService(sp.GetRequiredService<FurniturePlannerRepository>(), sp.GetRequiredService<IMapper>(), NullLogger<PlannerService>.Instance));
        Services.AddSingleton(sp => new FurnitureCatalogService(sp.GetRequiredService<FurniturePlannerRepository>(), sp.GetRequiredService<IMapper>(), NullLogger<FurnitureCatalogService>.Instance));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public void RoomPlans_EmptyStore_ShowsHeaderAndEmptyState()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        ConfigureServices(factory);

        var cut = Render<RoomPlans>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Room plans", cut.Markup);
            Assert.Contains("You haven't created any room plans yet.", cut.Markup);
        });
    }

    [Fact]
    public void FurnitureCatalog_EmptyStore_ShowsHeaderAndEmptyState()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        ConfigureServices(factory);

        var cut = Render<FurnitureCatalog>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Furniture catalog", cut.Markup);
            Assert.Contains("Your furniture catalog is empty.", cut.Markup);
        });
    }
}
