using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

public class CodeAssignmentServiceTests
{
    // Mirrors SchemaTests.NewContext(): the connection is not owned by the context, so callers
    // must dispose it themselves to keep the in-memory database alive for the test's duration.
    private static (FurniturePlannerContext Context, Microsoft.Data.Sqlite.SqliteConnection Connection) NewContext()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        var ctx = new FurniturePlannerContext(options);
        ctx.Database.Migrate();
        return (ctx, conn);
    }

    [Fact]
    public async Task RegisterVariantAsync_IsIdempotent_AndCreatesDraftModelState()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var service = new CodeAssignmentService(ctx);

        await service.RegisterVariantAsync("FJORD", "FJ3-__MATERIAL__:Fabric");
        await service.RegisterVariantAsync("FJORD", "FJ3-__MATERIAL__:Fabric");

        var rows = await service.GetForModelAsync("FJORD");
        Assert.Single(rows);
        Assert.Null(rows[0].SuggestedCode);

        var state = await ctx.ModelStates.SingleAsync(s => s.ModelCode == "FJORD");
        Assert.Equal(TradeItemState.Draft, state.State);
    }

    [Fact]
    public async Task GetModelStateAsync_UnseenModel_ReturnsDraft()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var service = new CodeAssignmentService(ctx);

        var state = await service.GetModelStateAsync("UNSEEN");

        Assert.Equal(TradeItemState.Draft, state);
    }

    [Fact]
    public async Task AssignAsync_WhileDraft_PersistsTrimmedCodeAndNotes()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var service = new CodeAssignmentService(ctx);

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
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var service = new CodeAssignmentService(ctx);

        await service.RegisterVariantAsync("FJORD", "FJ3-__MATERIAL__:Fabric");
        var template = (await service.GetForModelAsync("FJORD")).Single();

        await service.ReleaseModelAsync("FJORD");

        Assert.Equal(TradeItemState.Active, await service.GetModelStateAsync("FJORD"));
        await Assert.ThrowsAsync<TemplateFrozenException>(() => service.AssignAsync(template.Id, "18E", null));
    }

    [Fact]
    public async Task SuggestionsForModelAsync_ExcludesNullCodeRows()
    {
        var (ctx, conn) = NewContext();
        using var _ = conn;
        using var ctxDispose = ctx;
        var service = new CodeAssignmentService(ctx);

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
}
