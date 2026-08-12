using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Regression net for the startup crash: the Program.cs startup callback resolves
// ProductionUnitService inside a SYNCHRONOUS `using var scope`, which drags in the scoped
// HttpContextAuthenticationStateProvider (via ICurrentUser -> CurrentUser). A DI scope refuses
// to sync-dispose a service that only implements IAsyncDisposable, so the app died at boot the
// moment the provider lacked a sync Dispose. This wires the same graph the startup callback
// sees and disposes the scope synchronously, exactly like Program.cs does.
public class StartupScopeDisposalTests
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    [Fact]
    public void StartupScope_ResolvingProductionUnitService_SurvivesSyncDisposal()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IDbContextFactory<FurniturePlannerContext>>(new TestDbContextFactory(options));
        services.AddScoped<AuthenticationStateProvider, HttpContextAuthenticationStateProvider>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<PinnedCatalogueProvider>();
        services.AddScoped<ProductionUnitService>();

        using var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<ProductionUnitService>();

        // Pre-fix this throws InvalidOperationException ("only implements IAsyncDisposable")
        // from ServiceProviderEngineScope.Dispose - the exact startup crash.
        scope.Dispose();
    }
}
