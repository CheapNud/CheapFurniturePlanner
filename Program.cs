using CheapAvaloniaBlazor.Hosting;
using CheapAvaloniaBlazor.Extensions;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Mappings;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Repositories;
using CheapFurniturePlanner.Services;
using CheapHelpers.EF;
using CheapHelpers.Models.Entities;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CheapFurniturePlanner;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var builder = new CheapAvaloniaBlazor.Hosting.HostBuilder()
            .WithTitle("Cheap Furniture Planner")
            .WithDiagnostics()
            .EnableDevTools()
            .EnableContextMenu()
            .WithSize(1200, 800)
            .UseContentRoot(Directory.GetCurrentDirectory())
            .AddMudBlazor();

        // Configure Entity Framework
        var connectionString = GetConnectionString();

        builder.Services.AddDbContextFactory<FurniturePlannerContext>(options => options.UseSqlite(connectionString));

        // Apply EF migrations at startup (replaces the orphaned EnsureCreated maintenance service),
        // then seed the Fjord demo catalogue on first run. Both live in this single ConfigureServices
        // callback because CheapAvaloniaBlazor's HostBuilder keeps only the last registered callback,
        // so a second builder.ConfigureServices(...) call would silently replace the migration step.
        builder.ConfigureServices(serviceProvider =>
        {
            using var scope = serviceProvider.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FurniturePlannerContext>>();
            using var migrateContext = factory.CreateDbContext();
            migrateContext.Database.Migrate();

            if (!migrateContext.PublishedCatalogues.Any(c => c.IsCurrent))
            {
                var asm = typeof(Program).Assembly;
                using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json")
                    ?? throw new InvalidOperationException("Embedded Fjord seed catalogue resource not found.");
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var snapshot = CanonicalJson.Deserialize<CatalogueSnapshot>(json)
                    ?? throw new InvalidOperationException("Failed to deserialize the embedded Fjord seed catalogue.");
                var publishService = scope.ServiceProvider.GetRequiredService<CataloguePublishService>();
                var result = publishService.PublishAsync(snapshot).GetAwaiter().GetResult();
                if (!result.Success)
                {
                    throw new InvalidOperationException("Seed catalogue failed validation: " + string.Join("; ", result.Errors));
                }
            }
        });

        // Configure Mapster
        var config = new TypeAdapterConfig();
        FurniturePlannerMappingProfile.Configure(config);
        builder.Services.AddSingleton(config);
        builder.Services.AddScoped<IMapper, ServiceMapper>();

        // Add furniture planner services
        builder.Services.AddSingleton<ICatalogueSource, DbCatalogueSource>();
        builder.Services.AddScoped<CataloguePublishService>();
        builder.Services.AddScoped<FurniturePlannerRepository>();
        builder.Services.AddScoped<FurnitureCatalogService>();
        builder.Services.AddScoped<RoomPlanService>();
        builder.Services.AddScoped<PlannerService>();
        builder.Services.AddScoped<PricingService>();
        builder.Services.AddScoped<CodeAssignmentService>();

        // Run the app - all Avalonia complexity handled by the package
        builder.RunApp(args);
    }

    private static string GetConnectionString()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheapFurniturePlanner");
        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }
        var dbPath = Path.Combine(appDataPath, "furniture_planner.db");
        return $"Data Source={dbPath}";
    }
}
