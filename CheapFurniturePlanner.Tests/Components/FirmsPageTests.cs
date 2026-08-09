using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 5: the /firms admin page lists Firm (our own legal entities/ledgers) with a Collections
// editor per row. Harness mirrors UsersPageTests (bUnit + in-memory SQLite, Admin-only service).
public class FirmsPageTests : TestContext
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
        }
        return (new TestDbContextFactory(options), connection);
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory)
    {
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(sp => new FirmService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), new FakeCurrentUser("admin-1", Roles.Admin)));
        Services.AddSingleton(sp => new PartyService(sp.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>(), new FakeCurrentUser("admin-1", Roles.Admin)));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public void EmptyStore_ShowsEmptyState_AndNoHeaderAdd()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        ConfigureServices(factory);

        var cut = Render<FirmsPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No firms yet", cut.Markup);
            // Only the EmptyState's action button should render "Add firm" - the header Add
            // button is conditional on _firms.Count > 0 (UX-1 conditional-Add), so it must not
            // duplicate this text.
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(cut.Markup, "Add firm").Count);
        });
    }

    [Fact]
    public async Task SeededFirms_RenderRows_WithDefaultChip()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var firms = new FirmService(factory, new FakeCurrentUser("admin-1", Roles.Admin));
        await firms.AddFirmAsync(new() { Code = "ALP", Name = "Alpine Living" });
        await firms.AddFirmAsync(new() { Code = "URB", Name = "Urban Nest" });
        ConfigureServices(factory);

        var cut = Render<FirmsPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alpine Living", cut.Markup);
            Assert.Contains("Urban Nest", cut.Markup);
            // Exactly one row's Default cell is the chip (not the "Make default" button) -
            // target the chip content markup, since DataLabel="Default" is on both rows' cells.
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(cut.Markup, "mud-chip-content\">Default<").Count);
        });
    }
}
