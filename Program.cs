using CheapAvaloniaBlazor.Hosting;
using CheapFurniturePlanner.Data;
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
            .WithSize(1200, 800)
            .UseContentRoot(Directory.GetCurrentDirectory())
            .AddMudBlazor();

        // Configure Entity Framework
        var connectionString = GetConnectionString();

        builder.Services.AddDbContextFactory<FurniturePlannerContext>(options => options.UseSqlite(connectionString));

        // Configure Mapster
        var config = new TypeAdapterConfig();
        FurniturePlannerMappingProfile.Configure(config);
        builder.Services.AddSingleton(config);
        builder.Services.AddScoped<IMapper, ServiceMapper>();

        // Add furniture planner services
        builder.Services.AddScoped<FurniturePlannerRepository>();
        builder.Services.AddScoped<FurnitureCatalogService>();
        builder.Services.AddScoped<RoomPlanService>();
        builder.Services.AddScoped<PlannerService>();

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
