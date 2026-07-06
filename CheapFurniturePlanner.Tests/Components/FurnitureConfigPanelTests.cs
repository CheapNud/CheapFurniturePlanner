using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Shared;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.ViewModels;
using Microsoft.AspNetCore.Components;
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

    private CatalogueSnapshot ConfigureServices()
    {
        var snapshot = LoadFjordSnapshot();
        Services.AddMudServices();
        Services.AddSingleton<ICatalogueSource>(new FakeCatalogueSource(snapshot));
        Services.AddSingleton(sp => new PricingService(sp.GetRequiredService<ICatalogueSource>()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        // MudSelect renders its options into an overlay managed by MudBlazor's popover service, which
        // requires a MudPopoverProvider to be present somewhere in the render tree.
        RenderComponent<MudPopoverProvider>();

        return snapshot;
    }

    private static IRenderedComponent<MudSelect<string>> FindSelect(IRenderedComponent<FurnitureConfigPanel> cut, string optionDefinitionCode) =>
        cut.FindComponents<MudSelect<string>>().Single(c => c.Instance.Label == optionDefinitionCode);

    [Fact]
    public void Render_ShowsOneSelectPerVisibleOption_AndHidesTriggerGatedOption()
    {
        ConfigureServices();
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
        ConfigureServices();
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
        ConfigureServices();
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
        ConfigureServices();
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
        ConfigureServices();
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
        ConfigureServices();
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
        ConfigureServices();
        var placement = new FurniturePlannerViewModel { ElementCode = "DOES-NOT-EXIST", Name = "Ghost Sofa" };

        var cut = RenderComponent<FurnitureConfigPanel>(p => p.Add(x => x.Placement, placement));

        Assert.Contains(cut.FindAll(".mud-alert"), _ => true);
        Assert.Contains("unavailable in this catalogue", cut.Markup);
    }

    [Fact]
    public async Task BreakingConfigAfterValidPrice_ClearsCachedPersistedPrice()
    {
        ConfigureServices();
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
}
