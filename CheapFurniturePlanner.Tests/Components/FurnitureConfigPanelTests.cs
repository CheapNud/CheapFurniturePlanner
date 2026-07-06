using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Shared;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Exercises FurnitureConfigPanel end to end against the real embedded Fjord catalogue (same fixture
// ConfigurationResolverTests/PricingServiceTests use) and a real PricingService, so visibility rules,
// fabric selection, and live pricing are proven together rather than against a hand-rolled stub.
// xUnit creates a fresh instance of this class per [Fact] and disposes it afterwards (TestContext
// implements IDisposable), so each test gets its own bUnit render tree/service collection.
public class FurnitureConfigPanelTests : TestContext
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

    private static FurniturePlannerViewModel Fj3Placement() => new()
    {
        ElementCode = "FJ3",
        Selections = new Dictionary<string, string> { ["DEPTH"] = "STD", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" },
        FabricColorCode = "AQUA-BLUE",
    };

    // Registers every service FurnitureConfigPanel depends on, including the production-identity
    // bridge - it resolves/registers against a real (in-memory SQLite) DB, so callers must keep the
    // returned connection alive (and dispose it) for the duration of the test, mirroring
    // PlannerPagePanelTests/CodeAssignmentServiceTests.
    private (CatalogueSnapshot Snapshot, DbContextOptions<FurniturePlannerContext> DbOptions, SqliteConnection Connection) ConfigureServices()
    {
        var snapshot = LoadFjordSnapshot();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var dbOptions = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        using (var migrateContext = new FurniturePlannerContext(dbOptions))
        {
            migrateContext.Database.Migrate();
        }

        Services.AddMudServices();
        Services.AddSingleton<ICatalogueSource>(new FakeCatalogueSource(snapshot));
        Services.AddSingleton(sp => new PricingService(sp.GetRequiredService<ICatalogueSource>()));
        Services.AddSingleton<IDbContextFactory<FurniturePlannerContext>>(new TestDbContextFactory(dbOptions));
        Services.AddSingleton(sp => new CodeAssignmentService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        Services.AddSingleton(sp => new ProductionIdentityService(sp.GetRequiredService<ICatalogueSource>(), sp.GetRequiredService<CodeAssignmentService>()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        // MudSelect renders its options into an overlay managed by MudBlazor's popover service, which
        // requires a MudPopoverProvider to be present somewhere in the render tree.
        RenderComponent<MudPopoverProvider>();

        return (snapshot, dbOptions, conn);
    }

    private static IRenderedComponent<MudSelect<string>> FindSelect(IRenderedComponent<FurnitureConfigPanel> cut, string optionDefinitionCode) =>
        cut.FindComponents<MudSelect<string>>().Single(c => c.Instance.Label == optionDefinitionCode);

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    [Fact]
    public void Render_ShowsOneSelectPerVisibleOption_AndHidesTriggerGatedOption()
    {
        using var _ = ConfigureServices().Connection;
        var placement = Fj3Placement();

        var cut = RenderComponent<FurnitureConfigPanel>(p => p.Add(x => x.Placement, placement));

        var selects = cut.FindComponents<MudSelect<string>>();
        Assert.Equal(3, selects.Count); // DEPTH, MECH, STITCH visible; HEAD gated behind MECH=REC
        Assert.DoesNotContain(selects, c => c.Instance.Label == "HEAD");
        Assert.Contains(selects, c => c.Instance.Label == "DEPTH");
        Assert.Contains(selects, c => c.Instance.Label == "MECH");
        Assert.Contains(selects, c => c.Instance.Label == "STITCH");
    }

    [Fact]
    public async Task SelectingTrigger_RevealsGatedOption()
    {
        using var _ = ConfigureServices().Connection;
        var placement = Fj3Placement();

        var cut = RenderComponent<FurnitureConfigPanel>(p => p.Add(x => x.Placement, placement));

        var mech = FindSelect(cut, "MECH");
        await cut.InvokeAsync(() => mech.Instance.ValueChanged.InvokeAsync("REC"));

        var selectsAfter = cut.FindComponents<MudSelect<string>>();
        Assert.Contains(selectsAfter, c => c.Instance.Label == "HEAD");
        Assert.Equal("REC", placement.Selections["MECH"]);
    }

    [Fact]
    public async Task SelectingFabricChip_UpdatesDisplayedPrice()
    {
        using var _ = ConfigureServices().Connection;
        var placement = Fj3Placement();

        var cut = RenderComponent<FurnitureConfigPanel>(p => p.Add(x => x.Placement, placement));

        var priceBefore = placement.CachedUnitPrice;
        Assert.NotNull(priceBefore);

        // AQUA-BLUE (default) is the cheapest fabric group; TERRA-SAND belongs to a pricier group,
        // so switching to it must change the priced total.
        var terraChip = cut.FindAll(".fabric-swatch").Single(e => e.GetAttribute("title") == "Terra Sand");
        await cut.InvokeAsync(() => terraChip.Click());

        Assert.Equal("TERRA-SAND", placement.FabricColorCode);
        Assert.NotEqual(priceBefore, placement.CachedUnitPrice);
        Assert.Contains(placement.CachedUnitPrice!.Value.ToString("C2"), cut.Markup);
    }

    [Fact]
    public void InitialRender_DoesNotRaiseOnConfigured()
    {
        // Selecting an already-placed item to inspect it must not look like an edit: the initial/
        // selection-driven reprice in OnParametersSetAsync updates the display + cached price fields
        // but must not notify the parent, otherwise merely viewing an item would dirty the plan and
        // trigger a DB write (see PlannerPage.HandleConfigured).
        using var _ = ConfigureServices().Connection;
        var placement = Fj3Placement();
        var raisedCount = 0;

        var cut = RenderComponent<FurnitureConfigPanel>(p =>
        {
            p.Add(x => x.Placement, placement);
            p.Add(x => x.OnConfigured, EventCallback.Factory.Create(this, () => raisedCount++));
        });

        Assert.Equal(0, raisedCount);
        Assert.NotNull(placement.CachedUnitPrice); // display state still gets primed
    }

    [Fact]
    public async Task ChangingOption_RaisesOnConfigured()
    {
        using var _ = ConfigureServices().Connection;
        var placement = Fj3Placement();
        var raisedCount = 0;

        var cut = RenderComponent<FurnitureConfigPanel>(p =>
        {
            p.Add(x => x.Placement, placement);
            p.Add(x => x.OnConfigured, EventCallback.Factory.Create(this, () => raisedCount++));
        });

        var initialCount = raisedCount; // initial render reprices for display only - does not notify
        var stitch = FindSelect(cut, "STITCH");
        await cut.InvokeAsync(() => stitch.Instance.ValueChanged.InvokeAsync("CONTRAST"));

        Assert.True(raisedCount > initialCount);
    }

    [Fact]
    public async Task SelectingFabricChip_RaisesOnConfigured()
    {
        using var _ = ConfigureServices().Connection;
        var placement = Fj3Placement();
        var raisedCount = 0;

        var cut = RenderComponent<FurnitureConfigPanel>(p =>
        {
            p.Add(x => x.Placement, placement);
            p.Add(x => x.OnConfigured, EventCallback.Factory.Create(this, () => raisedCount++));
        });

        var initialCount = raisedCount;
        var terraChip = cut.FindAll(".fabric-swatch").Single(e => e.GetAttribute("title") == "Terra Sand");
        await cut.InvokeAsync(() => terraChip.Click());

        Assert.True(raisedCount > initialCount);
    }

    [Fact]
    public void DanglingElementCode_ShowsUnavailableRegion()
    {
        using var _ = ConfigureServices().Connection;
        var placement = new FurniturePlannerViewModel { ElementCode = "DOES-NOT-EXIST", Name = "Ghost Sofa" };

        var cut = RenderComponent<FurnitureConfigPanel>(p => p.Add(x => x.Placement, placement));

        Assert.Contains(cut.FindAll(".mud-alert"), _ => true);
        Assert.Contains("unavailable in this catalogue", cut.Markup);
    }

    [Fact]
    public async Task BreakingConfigAfterValidPrice_ClearsCachedPersistedPrice()
    {
        using var _ = ConfigureServices().Connection;
        var placement = Fj3Placement();

        var cut = RenderComponent<FurnitureConfigPanel>(p => p.Add(x => x.Placement, placement));

        // Sanity check: the panel priced successfully on first render, so the persisted cache fields
        // (which PlannerService writes into the saved plan) hold a real price.
        Assert.NotNull(placement.CachedUnitPrice);
        Assert.NotNull(placement.CachedVariantCode);

        // Break the configuration by pointing at a fabric color that no longer exists in the
        // catalogue, then trigger a reprice via a normal option change (as a user interaction would).
        placement.FabricColorCode = "DOES-NOT-EXIST";
        var stitch = FindSelect(cut, "STITCH");
        await cut.InvokeAsync(() => stitch.Instance.ValueChanged.InvokeAsync("CONTRAST"));

        // The pricing failure must clear the persisted cache fields, not just the panel's local
        // display state - otherwise saving while this invalid config is showing would record the
        // last-known-good price as if it were still valid.
        Assert.Null(placement.CachedUnitPrice);
        Assert.Null(placement.CachedVariantCode);
        Assert.Contains(cut.FindAll(".mud-alert"), _ => true);
    }

    [Fact]
    public void Render_ShowsProductionCodeLine_ForConfiguredPlacement()
    {
        var (snapshot, _, conn) = ConfigureServices();
        using var _ = conn;
        var placement = Fj3Placement();

        var cut = RenderComponent<FurnitureConfigPanel>(p => p.Add(x => x.Placement, placement));

        var config = new ProductConfiguration("FJORD",
            [new ElementSelection("FJ3", 1, placement.Selections, placement.FabricColorCode)]);
        var expected = ProductionIdentityResolver.Resolve(snapshot, config, new Dictionary<string, string>(), Domain.Catalog.TradeItemState.Draft)[0];

        Assert.Contains(cut.FindAll(".production-code-row"), _ => true);
        Assert.Contains(expected.EffectiveCode, cut.Markup);
        Assert.Contains(cut.FindComponents<MudChip<bool>>(), c => c.Markup.Contains("composed"));
    }

    [Fact]
    public async Task Render_ReleasedAssignment_ShowsReleasedBadgeAndAssignedCode()
    {
        var (snapshot, dbOptions, conn) = ConfigureServices();
        using var _ = conn;
        var placement = Fj3Placement();

        var config = new ProductConfiguration("FJORD",
            [new ElementSelection("FJ3", 1, placement.Selections, placement.FabricColorCode)]);
        var variantCode = ProductionIdentityResolver.Resolve(snapshot, config, new Dictionary<string, string>(), Domain.Catalog.TradeItemState.Draft)[0].VariantCode;

        var seedAssignments = new CodeAssignmentService(new TestDbContextFactory(dbOptions));
        await seedAssignments.RegisterVariantAsync("FJORD", variantCode);
        var template = (await seedAssignments.GetForModelAsync("FJORD")).Single(t => t.VariantCode == variantCode);
        await seedAssignments.AssignAsync(template.Id, "18E", null);
        await seedAssignments.ReleaseModelAsync("FJORD");

        var cut = RenderComponent<FurnitureConfigPanel>(p => p.Add(x => x.Placement, placement));

        Assert.Contains("18E", cut.Markup);
        Assert.Contains(cut.FindComponents<MudChip<bool>>(), c => c.Markup.Contains("released"));
    }
}
