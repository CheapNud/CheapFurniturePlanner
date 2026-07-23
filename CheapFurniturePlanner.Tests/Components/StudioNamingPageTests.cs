using Bunit;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Exercises the studio's per-model naming drill-in: for a Draft model it lists the
// enumerator's BOM-significant variants per element with editable code fields whose commits persist
// through the real ArticleAuthoringService; for a released (Active) model, editing is frozen (Disabled
// fields + the frozen alert). Runs against a real ArticleAuthoringService + ModelPublishService over
// in-memory SQLite, following the StudioPageTests/ArticleAuthoringServiceTests harness pattern.
public class StudioNamingPageTests : TestContext
{
    private const string Studio = "FJORD-STUDIO";
    private const string Released = "FJORD";

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (new TestDbContextFactory(options), conn);
    }

    // Seeds the two demo model states exactly as Program.cs does on first run: FJORD Active,
    // FJORD-STUDIO Draft.
    private static async Task SeedModelStatesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Released, State = TradeItemState.Active });
        db.ModelStates.Add(new ModelStateRecord { ModelCode = Studio, State = TradeItemState.Draft });
        await db.SaveChangesAsync();
    }

    // The page now loads the model/elements from the authoring store rather than the embedded seed
    // directly, so the store must be seeded from that same embedded seed for the page to find
    // FJORD/FJORD-STUDIO.
    private async Task<ArticleAuthoringService> ConfigureServicesAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        var store = new AuthoringCatalogueStore(factory);
        await store.SeedFromAsync(SeedCatalogue.Load());
        var source = new DbCatalogueSource(factory);
        var publish = new ModelPublishService(factory, new CataloguePublishService(factory, source), source, store);
        var naming = new ArticleAuthoringService(store, publish);

        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(store);
        Services.AddSingleton<ICatalogueSource>(source);
        Services.AddSingleton(publish);
        Services.AddSingleton(naming);
        JSInterop.Mode = JSRuntimeMode.Loose;

        Render<MudPopoverProvider>();
        return naming;
    }

    [Fact]
    public async Task Render_DraftModel_ListsEnumeratedVariants_WithEditableCodeFields()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        await ConfigureServicesAsync(factory);

        var model = SeedCatalogue.Load().Models.Single(m => m.Code == Studio);
        var snapshot = SeedCatalogue.Load();
        var expectedRowCount = model.Elements.Sum(e => VariantEnumerator.Enumerate(e, snapshot).Count);

        var cut = Render<StudioNamingPage>(p => p.Add(x => x.ModelCode, Studio));

        Assert.Contains("Fjord Studio", cut.Markup);
        Assert.DoesNotContain("frozen", cut.Markup);

        var fields = cut.FindComponents<MudTextField<string>>();
        Assert.Equal(expectedRowCount, fields.Count);
        Assert.All(fields, f => Assert.False(f.Instance.Disabled));
    }

    [Fact]
    public async Task EnteringCode_OnDraftModel_PersistsThroughArticleAuthoringService()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        var naming = await ConfigureServicesAsync(factory);

        var model = SeedCatalogue.Load().Models.Single(m => m.Code == Studio);
        var snapshot = SeedCatalogue.Load();
        var firstElement = model.Elements[0];
        var expectedVariantCode = VariantEnumerator.Enumerate(firstElement, snapshot)[0].VariantCode;

        var cut = Render<StudioNamingPage>(p => p.Add(x => x.ModelCode, Studio));

        // MudTable preserves Items order (no sorting configured), so the first-rendered field belongs
        // to the first element's first enumerated row - matching expectedVariantCode above.
        var firstField = cut.FindComponents<MudTextField<string>>().First();
        await cut.InvokeAsync(() => firstField.Instance.ValueChanged.InvokeAsync("STUDIO-A"));

        var names = await naming.NamesForModelAsync(Studio);
        Assert.Equal("STUDIO-A", names[expectedVariantCode]);
    }

    [Fact]
    public async Task Render_ActiveModel_CodeFieldsDisabled_AndFrozenAlertShown()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await SeedModelStatesAsync(factory);
        await ConfigureServicesAsync(factory);

        var cut = Render<StudioNamingPage>(p => p.Add(x => x.ModelCode, Released));

        Assert.Contains("frozen", cut.Markup);
        Assert.Contains(TradeItemState.Active.ToString(), cut.Markup);

        var fields = cut.FindComponents<MudTextField<string>>();
        Assert.NotEmpty(fields);
        Assert.All(fields, f => Assert.True(f.Instance.Disabled));
    }

    [Fact]
    public async Task Render_UnknownModelCode_ShowsNotFound()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        await ConfigureServicesAsync(factory);

        var cut = Render<StudioNamingPage>(p => p.Add(x => x.ModelCode, "NOPE"));

        Assert.Contains("not found", cut.Markup);
    }
}
