using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Exercises the modellenkamer authoring page end to end against the real embedded Fjord catalogue
// and a real CodeAssignmentService over in-memory SQLite, mirroring FurnitureConfigPanelTests: the
// page is the operator-facing surface for assigning/freezing 18E codes, so its gating (Draft vs.
// Active/Discontinued) has to be proven against the actual service transitions, not a stub.
public class ModellenkamerPageTests : TestContext
{
    private sealed class FakeCatalogueSource(CatalogueSnapshot snapshot) : ICatalogueSource
    {
        public Task<CatalogueSnapshot> GetCurrentAsync() => Task.FromResult(snapshot);

        public void Invalidate() { }
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
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

    // Same FJ3 config FurnitureConfigPanelTests prices (DEPTH:STD, MECH:NONE - both AffectsBom;
    // STITCH is not BOM-significant so it never reaches the variant code); derived via the real
    // resolver rather than hardcoded so a change to the encoding can't silently desync the test.
    private static string Fj3VariantCode(CatalogueSnapshot snapshot)
    {
        var selections = new Dictionary<string, string> { ["DEPTH"] = "STD", ["MECH"] = "NONE", ["STITCH"] = "PLAIN" };
        var config = new ProductConfiguration("FJORD", [new ElementSelection("FJ3", 1, selections, "AQUA-BLUE")]);
        return ProductionIdentityResolver.Resolve(snapshot, config, new Dictionary<string, string>(), TradeItemState.Draft)[0].VariantCode;
    }

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
        Services.AddSingleton<IDbContextFactory<FurniturePlannerContext>>(new TestDbContextFactory(dbOptions));
        Services.AddSingleton(sp => new CodeAssignmentService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        // MudSelect renders its options into an overlay managed by MudBlazor's popover service, which
        // requires a MudPopoverProvider to be present somewhere in the render tree.
        RenderComponent<MudPopoverProvider>();

        return (snapshot, dbOptions, conn);
    }

    // The page wires the model MudSelect's ValueChanged straight to its selection handler, so
    // invoking the callback directly (as FurnitureConfigPanelTests does for its option selects)
    // drives the same code path a real click through the popover would.
    private static async Task SelectModelAsync(IRenderedComponent<ModellenkamerPage> cut, string modelCode)
    {
        var select = cut.FindComponent<MudSelect<string>>();
        await cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(modelCode));
    }

    [Fact]
    public async Task SelectingDraftModel_ListsVariantRow_WithEditableSuggestedCodeField()
    {
        var (snapshot, dbOptions, conn) = ConfigureServices();
        using var _ = conn;
        var variantCode = Fj3VariantCode(snapshot);
        var seedAssignments = new CodeAssignmentService(new TestDbContextFactory(dbOptions));
        await seedAssignments.RegisterVariantAsync("FJORD", variantCode);

        var cut = RenderComponent<ModellenkamerPage>();
        await SelectModelAsync(cut, "FJORD");

        Assert.Contains(variantCode, cut.Markup);
        var codeField = cut.FindComponents<MudTextField<string>>()[0];
        Assert.False(codeField.Instance.Disabled);
        Assert.Contains("Composed", cut.Markup);
    }

    [Fact]
    public async Task EnteringCodeAndAssigning_PersistsCodeViaCodeAssignmentService()
    {
        var (snapshot, dbOptions, conn) = ConfigureServices();
        using var _ = conn;
        var variantCode = Fj3VariantCode(snapshot);
        var seedAssignments = new CodeAssignmentService(new TestDbContextFactory(dbOptions));
        await seedAssignments.RegisterVariantAsync("FJORD", variantCode);

        var cut = RenderComponent<ModellenkamerPage>();
        await SelectModelAsync(cut, "FJORD");

        var codeField = cut.FindComponents<MudTextField<string>>()[0];
        await cut.InvokeAsync(() => codeField.Instance.ValueChanged.InvokeAsync("18E"));

        var assignButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assign");
        await cut.InvokeAsync(() => assignButton.Click());

        var persisted = (await seedAssignments.GetForModelAsync("FJORD")).Single(t => t.VariantCode == variantCode);
        Assert.Equal("18E", persisted.SuggestedCode);
        Assert.Contains("Provisional", cut.Markup);
    }

    [Fact]
    public async Task AfterReleaseModel_SuggestedCodeFieldIsDisabled_AndBadgeReadsReleased()
    {
        var (snapshot, dbOptions, conn) = ConfigureServices();
        using var _ = conn;
        var variantCode = Fj3VariantCode(snapshot);
        var seedAssignments = new CodeAssignmentService(new TestDbContextFactory(dbOptions));
        await seedAssignments.RegisterVariantAsync("FJORD", variantCode);
        var template = (await seedAssignments.GetForModelAsync("FJORD")).Single(t => t.VariantCode == variantCode);
        await seedAssignments.AssignAsync(template.Id, "18E", null);

        var cut = RenderComponent<ModellenkamerPage>();
        await SelectModelAsync(cut, "FJORD");

        // Released out-of-band (as a second operator/tab would) rather than through the page's own
        // Release button, so this test stays focused on the gating re-render rather than the
        // MudMessageBox confirmation flow. Re-selecting the same model forces the page to reload
        // state/templates from the service, the same way a fresh navigation to the page would.
        await seedAssignments.ReleaseModelAsync("FJORD");
        await SelectModelAsync(cut, "FJORD");

        var codeField = cut.FindComponents<MudTextField<string>>()[0];
        Assert.True(codeField.Instance.Disabled);
        Assert.Contains("Released", cut.Markup);
        Assert.Contains("frozen", cut.Markup);
    }
}
