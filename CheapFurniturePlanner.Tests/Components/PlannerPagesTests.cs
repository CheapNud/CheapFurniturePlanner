using Bunit;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Components.Shared;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Mappings;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Repositories;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.ViewModels;
using Mapster;
using MapsterMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
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

    // Returns the rendered MudDialogProvider: an inline `<MudDialog @bind-Visible>` (both RoomPlans'
    // and FurnitureCatalog's edit dialogs) still routes through the injected IDialogService under the
    // hood (MudDialog.OnAfterRenderAsync calls ShowAsync() once Visible flips true), so its
    // DialogContent renders inside the DialogProvider's own component tree, not inside the page's -
    // a test that opens one of these dialogs must search the provider, not the page's `cut`.
    private IRenderedComponent<MudBlazor.MudDialogProvider> ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
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
        var dialogProvider = Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
        return dialogProvider;
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

    // UX-2 final-review fix 1: the edit dialog's "Active in Catalog" switch used @bind-Checked -
    // MudSwitch<bool> has no Checked/CheckedChanged pair, so the binding silently no-ops (same root
    // cause as MainLayoutTests' dark-mode switch). Unlike the dark-mode switch this one is
    // data-affecting: flipping it in the dialog and saving never reached the database. Proven
    // black-box here - flip the switch, save, then reload the page's own active-only list (the
    // service filters IsActive==true) and check the item actually dropped out of it, rather than
    // poking the switch's own rendered state.
    [Fact]
    public async Task FurnitureCatalog_EditDialog_TogglingIsActiveSwitchOff_RemovesItemFromActiveListOnSave()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;

        var mapsterConfig = new TypeAdapterConfig();
        FurniturePlannerMappingProfile.Configure(mapsterConfig);
        IMapper mapper = new Mapper(mapsterConfig);
        var repository = new FurniturePlannerRepository(factory);
        var seedingCatalogService = new FurnitureCatalogService(repository, mapper, NullLogger<FurnitureCatalogService>.Instance);
        await seedingCatalogService.AddFurnitureItemAsync(new FurnitureCatalogViewModel
        {
            Code = "SOFA-1",
            Name = "Test Sofa",
            Type = FurnitureType.Sofa,
            Width = 200,
            Length = 90,
            Height = 80,
            IsActive = true
        });

        var dialogProvider = ConfigureServices(factory);

        var cut = Render<FurnitureCatalog>();
        cut.WaitForAssertion(() => Assert.Contains("Test Sofa", cut.Markup));

        var editButton = cut.FindComponents<MudIconButton>()
            .Single(b => Equals(b.Instance.Icon, Icons.Material.Filled.Edit));
        await cut.InvokeAsync(() => editButton.Find("button").Click());

        dialogProvider.WaitForAssertion(() => Assert.Contains("Active in Catalog", dialogProvider.Markup));
        var isActiveSwitch = dialogProvider.FindComponents<MudSwitch<bool>>()
            .Single(s => s.Instance.Label == "Active in Catalog");
        await dialogProvider.InvokeAsync(() => isActiveSwitch.Instance.ValueChanged.InvokeAsync(false));

        var saveButton = dialogProvider.FindComponents<ProgressButton>().Single(b => b.Markup.Contains("Save"));
        await dialogProvider.InvokeAsync(() => saveButton.Find("button").Click());

        cut.WaitForAssertion(() => Assert.DoesNotContain("Test Sofa", cut.Markup));
    }
}
