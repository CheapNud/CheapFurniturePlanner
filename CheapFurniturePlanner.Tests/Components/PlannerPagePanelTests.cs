using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Components.Shared;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Mappings;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Repositories;
using CheapFurniturePlanner.Services;
using Mapster;
using MapsterMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using System.Text.Json;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Task 6 smoke test: the two-column layout wired into PlannerPage must keep the config panel hidden
// until an item is selected, then host FurnitureConfigPanel for the selected placement. This exercises
// the real selection wiring (FurniturePlannerContainer -> SelectedItemChanged -> PlannerPage._selectedItem)
// rather than poking private state, against a real (in-memory SQLite) DB and the embedded demo catalogue.
public class PlannerPagePanelTests : TestContext
{
    private sealed class FakeCatalogueSource(CatalogueSnapshot snapshot) : ICatalogueSource
    {
        public Task<CatalogueSnapshot> GetCurrentAsync() => Task.FromResult(snapshot);

        public void Invalidate() { }
    }

    private static CatalogueSnapshot LoadFjordSnapshot()
    {
        var asm = typeof(CataloguePublishService).Assembly;
        using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json")
            ?? throw new InvalidOperationException("Embedded resource 'CheapFurniturePlanner.Seed.demo-catalogue.json' not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return CanonicalJson.Deserialize<CatalogueSnapshot>(json)
            ?? throw new InvalidOperationException("Failed to deserialize embedded demo-catalogue.json.");
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    // Mirrors SchemaTests/PlannerConfigPersistenceTests: the connection is not owned by any single
    // context, so the caller must keep it open (and dispose it) for the duration of the test.
    private static (DbContextOptions<FurniturePlannerContext> Options, SqliteConnection Connection, int RoomId) SeedDatabase(
        bool withPlacement,
        string fabricColorCode = "AQUA-BLUE",
        Dictionary<string, string>? selections = null)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;

        int roomId;
        using (var seedContext = new FurniturePlannerContext(options))
        {
            seedContext.Database.Migrate();
            var room = new RoomPlan { Name = "Living Room", Width = 400, Height = 300 };
            seedContext.RoomPlans.Add(room);
            seedContext.SaveChanges();
            roomId = room.Id;

            if (withPlacement)
            {
                seedContext.PlannerFurnitureItems.Add(new PlannerFurnitureItem
                {
                    RoomPlanId = roomId,
                    UIId = 1,
                    X = 20,
                    Y = 20,
                    ElementCode = "FJ2",
                    CatalogueVersion = "1",
                    FabricColorCode = fabricColorCode,
                    SelectionsJson = JsonSerializer.Serialize(selections ?? new Dictionary<string, string> { ["DEPTH"] = "STD" }),
                    CreatedAt = DateTime.UtcNow
                });
                seedContext.SaveChanges();
            }
        }

        return (options, conn, roomId);
    }

    // Wires every service PlannerPage depends on against the given DB + the embedded demo catalogue.
    private void ConfigureServices(DbContextOptions<FurniturePlannerContext> options)
    {
        var mapsterConfig = new TypeAdapterConfig();
        FurniturePlannerMappingProfile.Configure(mapsterConfig);
        IMapper mapper = new Mapper(mapsterConfig);
        var repository = new FurniturePlannerRepository(new TestDbContextFactory(options));

        Services.AddMudServices();
        Services.AddSingleton(repository);
        Services.AddSingleton(mapper);
        Services.AddSingleton(sp => new RoomPlanService(sp.GetRequiredService<FurniturePlannerRepository>(), sp.GetRequiredService<IMapper>(), NullLogger<RoomPlanService>.Instance));
        Services.AddSingleton(sp => new PlannerService(sp.GetRequiredService<FurniturePlannerRepository>(), sp.GetRequiredService<IMapper>(), NullLogger<PlannerService>.Instance));
        Services.AddSingleton(sp => new FurnitureCatalogService(sp.GetRequiredService<FurniturePlannerRepository>(), sp.GetRequiredService<IMapper>(), NullLogger<FurnitureCatalogService>.Instance));
        Services.AddSingleton<ICatalogueSource>(new FakeCatalogueSource(LoadFjordSnapshot()));
        Services.AddSingleton(sp => new PricingService(sp.GetRequiredService<ICatalogueSource>()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        // MudSelect (used by both the room-settings dialog and the config panel's option dropdowns)
        // renders into an overlay that requires a MudPopoverProvider somewhere in the render tree.
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public void NothingSelected_ShowsPlaceholder_AndNoConfigPanel()
    {
        var (options, conn, roomId) = SeedDatabase(withPlacement: false);
        using var _ = conn;
        ConfigureServices(options);

        var cut = RenderComponent<PlannerPage>(p => p.Add(x => x.RoomPlanId, roomId));

        Assert.Empty(cut.FindComponents<FurnitureConfigPanel>());
        Assert.Contains("Select an element to configure", cut.Markup);
    }

    [Fact]
    public void SelectingPlacedElement_HostsConfigPanel_ForThatPlacement()
    {
        var (options, conn, roomId) = SeedDatabase(withPlacement: true);
        using var _ = conn;
        ConfigureServices(options);

        var cut = RenderComponent<PlannerPage>(p => p.Add(x => x.RoomPlanId, roomId));
        Assert.Empty(cut.FindComponents<FurnitureConfigPanel>()); // nothing selected yet

        var furnitureItem = cut.Find(".furniture-item");
        cut.InvokeAsync(() => furnitureItem.Click());

        var panel = Assert.Single(cut.FindComponents<FurnitureConfigPanel>());
        Assert.Equal("FJ2", panel.Instance.Placement?.ElementCode);
        Assert.DoesNotContain("Select an element to configure", cut.Markup);
    }

    // F1 regression: PlannerFurnitureItem persists no Name/dimensions for element placements, so the
    // Mapster map alone leaves them empty/null after a reload. PlannerPage.LoadRoomPlan must hydrate
    // them (via PlannerElementHydrator) against the current catalogue snapshot so the reloaded
    // placement doesn't render as an invisible 0x0 box.
    [Fact]
    public void LoadingRoomPlan_HydratesElementPlacementNameAndDimensions_FromCatalogueSnapshot()
    {
        var (options, conn, roomId) = SeedDatabase(withPlacement: true);
        using var _ = conn;
        ConfigureServices(options);

        var cut = RenderComponent<PlannerPage>(p => p.Add(x => x.RoomPlanId, roomId));

        var furnitureItem = cut.Find(".furniture-item");
        cut.InvokeAsync(() => furnitureItem.Click());

        var panel = Assert.Single(cut.FindComponents<FurnitureConfigPanel>());
        var placement = panel.Instance.Placement!;

        Assert.False(string.IsNullOrEmpty(placement.Name));
        Assert.Equal("Fjord 2-Seat", placement.Name);
        Assert.NotNull(placement.FurnitureWidth);
        Assert.NotNull(placement.FurnitureLength);
        Assert.NotNull(placement.FurnitureHeight);
        Assert.Equal(180.0, placement.FurnitureWidth);
        Assert.Equal(95.0, placement.FurnitureLength);
        Assert.Equal(80.0, placement.FurnitureHeight);
    }

    // F2 + F3 regression: duplicating a configured element must (F2) keep its selections/fabric
    // instead of resetting to catalogue defaults, and (F3) hand selection over to the duplicate so the
    // config panel follows it. The seeded configuration is deliberately all non-default values (FJ2's
    // defaults are DEPTH=STD/MECH=NONE/STITCH=PLAIN/fabric=AQUA-BLUE) so a "reset to defaults"
    // regression is provably caught rather than accidentally matching the defaults.
    [Fact]
    public void DuplicatingSelectedElement_PreservesConfig_AndSelectsTheDuplicate()
    {
        var (options, conn, roomId) = SeedDatabase(
            withPlacement: true,
            fabricColorCode: "TERRA-CLAY",
            selections: new Dictionary<string, string>
            {
                ["DEPTH"] = "DEEP",
                ["MECH"] = "REC",
                ["HEAD"] = "HS2",
                ["STITCH"] = "CONTRAST"
            });
        using var _ = conn;
        ConfigureServices(options);

        var cut = RenderComponent<PlannerPage>(p => p.Add(x => x.RoomPlanId, roomId));

        var furnitureItem = cut.Find(".furniture-item");
        cut.InvokeAsync(() => furnitureItem.Click());

        var originalPanel = Assert.Single(cut.FindComponents<FurnitureConfigPanel>());
        var original = originalPanel.Instance.Placement!;
        Assert.Equal("TERRA-CLAY", original.FabricColorCode);
        Assert.Equal("DEEP", original.Selections["DEPTH"]);

        var duplicateButton = cut.Find("[title='Duplicate']");
        cut.InvokeAsync(() => duplicateButton.Click());

        Assert.Equal(2, cut.FindAll(".furniture-item").Count);

        var panelAfterDuplicate = Assert.Single(cut.FindComponents<FurnitureConfigPanel>());
        var duplicate = panelAfterDuplicate.Instance.Placement!;

        // F3: the config panel must now follow the duplicate, not stay pinned on the original.
        Assert.NotSame(original, duplicate);

        // F2: the duplicate must retain the original's configuration rather than reset to defaults.
        Assert.Equal("FJ2", duplicate.ElementCode);
        Assert.Equal("TERRA-CLAY", duplicate.FabricColorCode);
        Assert.Equal("DEEP", duplicate.Selections["DEPTH"]);
        Assert.Equal("REC", duplicate.Selections["MECH"]);
        Assert.Equal("HS2", duplicate.Selections["HEAD"]);
        Assert.Equal("CONTRAST", duplicate.Selections["STITCH"]);
    }
}
