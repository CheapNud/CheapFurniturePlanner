using Avalonia.Markup.Xaml;
using CheapFurniturePlanner.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CheapFurniturePlanner.Services;

// Background service for database maintenance
public class DatabaseMaintenanceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseMaintenanceService> _logger;

    public DatabaseMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<DatabaseMaintenanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure database is created and migrated
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FurniturePlannerContext>();

        try
        {
            await context.Database.EnsureCreatedAsync(stoppingToken);
            _logger.LogInformation("Database initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database");
        }

        // Perform periodic maintenance (once per hour)
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

            try
            {
                using var maintenanceScope = _scopeFactory.CreateScope();
                var maintenanceContext = maintenanceScope.ServiceProvider.GetRequiredService<FurniturePlannerContext>();

                // Example maintenance: Clean up old temporary data
                // await CleanupOldData(maintenanceContext, stoppingToken);

                _logger.LogDebug("Database maintenance completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database maintenance");
            }
        }
    }
}