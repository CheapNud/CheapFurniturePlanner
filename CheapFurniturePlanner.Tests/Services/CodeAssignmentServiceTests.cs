using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

public class CodeAssignmentServiceTests
{
    // Mirrors DbCatalogueSourceTests.NewFactory(): the connection is not owned by any single context
    // handed out by the factory, so callers must dispose it themselves to keep the in-memory database
    // alive for the test's duration. CodeAssignmentService now depends on IDbContextFactory (matching
    // Program.cs's AddDbContextFactory registration) rather than a directly-injected context.
    private static (IDbContextFactory<FurniturePlannerContext> Factory, Microsoft.Data.Sqlite.SqliteConnection Connection) NewFactory()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (new TestDbContextFactory(options), conn);
    }

    [Fact]
    public async Task RegisterVariantAsync_IsIdempotent_AndCreatesDraftModelState()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new CodeAssignmentService(factory);

        await service.RegisterVariantAsync("FJORD", "FJ3-__MATERIAL__:Fabric");
        await service.RegisterVariantAsync("FJORD", "FJ3-__MATERIAL__:Fabric");

        var rows = await service.GetForModelAsync("FJORD");
        Assert.Single(rows);
        Assert.Null(rows[0].SuggestedCode);

        var state = await service.GetModelStateAsync("FJORD");
        Assert.Equal(TradeItemState.Draft, state);
    }

    [Fact]
    public async Task GetModelStateAsync_UnseenModel_ReturnsDraft()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new CodeAssignmentService(factory);

        var state = await service.GetModelStateAsync("UNSEEN");

        Assert.Equal(TradeItemState.Draft, state);
    }

    [Fact]
    public async Task AssignAsync_WhileDraft_PersistsTrimmedCodeAndNotes()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new CodeAssignmentService(factory);

        await service.RegisterVariantAsync("FJORD", "FJ3-__MATERIAL__:Fabric");
        var template = (await service.GetForModelAsync("FJORD")).Single();

        await service.AssignAsync(template.Id, "  18E  ", "  note  ");

        var suggestions = await service.SuggestionsForModelAsync("FJORD");
        Assert.Equal("18E", suggestions["FJ3-__MATERIAL__:Fabric"]);

        var updated = (await service.GetForModelAsync("FJORD")).Single();
        Assert.Equal("18E", updated.SuggestedCode);
        Assert.Equal("note", updated.Notes);
    }

    [Fact]
    public async Task ReleaseModelAsync_FreezesModel_SubsequentAssignThrows()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new CodeAssignmentService(factory);

        await service.RegisterVariantAsync("FJORD", "FJ3-__MATERIAL__:Fabric");
        var template = (await service.GetForModelAsync("FJORD")).Single();

        await service.ReleaseModelAsync("FJORD");

        Assert.Equal(TradeItemState.Active, await service.GetModelStateAsync("FJORD"));
        await Assert.ThrowsAsync<TemplateFrozenException>(() => service.AssignAsync(template.Id, "18E", null));
    }

    [Fact]
    public async Task SuggestionsForModelAsync_ExcludesNullCodeRows()
    {
        var (factory, conn) = NewFactory();
        using var _ = conn;
        var service = new CodeAssignmentService(factory);

        await service.RegisterVariantAsync("FJORD", "FJ3-__MATERIAL__:Fabric");
        await service.RegisterVariantAsync("FJORD", "FJ3-__MATERIAL__:Leather");
        var withoutCode = (await service.GetForModelAsync("FJORD")).Single(t => t.VariantCode == "FJ3-__MATERIAL__:Leather");
        var withCode = (await service.GetForModelAsync("FJORD")).Single(t => t.VariantCode == "FJ3-__MATERIAL__:Fabric");
        await service.AssignAsync(withCode.Id, "18E", null);

        var suggestions = await service.SuggestionsForModelAsync("FJORD");

        Assert.Single(suggestions);
        Assert.False(suggestions.ContainsKey("FJ3-__MATERIAL__:Leather"));
        Assert.Equal("18E", suggestions["FJ3-__MATERIAL__:Fabric"]);
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
