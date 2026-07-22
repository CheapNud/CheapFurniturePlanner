using CheapAvaloniaBlazor.Hosting;
using CheapAvaloniaBlazor.Extensions;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Mappings;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Repositories;
using CheapFurniturePlanner.Services;
using CheapHelpers.Blazor.Pages.Account;
using CheapHelpers.Blazor.Services;
using CheapHelpers.EF;
using CheapHelpers.Models.Entities;
using CheapHelpers.Services.Email;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
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
            .AddMudBlazor()
            // Identity middleware + the Account controller endpoints. The host only exposes
            // these two hook points into its pipeline (ConfigurePipeline runs after UseRouting,
            // before endpoints are mapped; ConfigureEndpoints runs after MapRazorComponents).
            .ConfigurePipeline(app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            })
            .ConfigureEndpoints(app => app.MapControllers());

        // Configure Entity Framework
        var connectionString = GetConnectionString();

        builder.Services.AddDbContextFactory<FurniturePlannerContext>(options => options.UseSqlite(connectionString));

        // Identity: relaxed password policy (desktop app, not internet-facing) and deliberate
        // opt-out of failed-attempt lockout - deactivation (Task 2) sets LockoutEnd directly instead.
        builder.Services.AddIdentity<FurnitureUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 6;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireDigit = false;
            options.Lockout.AllowedForNewUsers = false;
        })
            .AddEntityFrameworkStores<FurniturePlannerContext>()
            .AddDefaultTokenProviders();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login";
        });

        builder.Services.AddControllers();

        // Registrations the CheapHelpers AccountController needs. Not going through
        // AddCheapHelpersBlazor here - it would re-register MudBlazor (already added via
        // .AddMudBlazor() above) and re-register the DbContextFactory already set up here;
        // these three are the only pieces the controller actually requires.
        builder.Services.AddSingleton<IEmailService, NullEmailService>();
        builder.Services.AddSingleton(new AccountRouteOptions { LoginRoute = "/login" });
        builder.Services.AddScoped<UserService<FurnitureUser, FurniturePlannerContext>>();

        builder.Services.AddScoped<SetupState>();

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
            RoleSeeder.SeedAsync(migrateContext).GetAwaiter().GetResult();
            scope.ServiceProvider.GetRequiredService<VariantNamingAbsorber>().AbsorbAsync().GetAwaiter().GetResult();

            // Seed the authoring store from the embedded demo catalogue if it hasn't been seeded
            // already - the store is the sole authoring source from here on. This runs regardless
            // of published-catalogue state (not just on first run) so a DB created by a pre-authoring-store
            // build, which already has a current published catalogue, still gets its authoring store
            // populated on upgrade instead of leaving the Studio empty forever; the IsSeededAsync
            // check makes this a no-op on every subsequent startup.
            CatalogueSnapshot? seedCatalogue = null;
            var authoringStore = scope.ServiceProvider.GetRequiredService<AuthoringCatalogueStore>();
            if (!authoringStore.IsSeededAsync().GetAwaiter().GetResult())
            {
                seedCatalogue = SeedCatalogue.Load();
                authoringStore.SeedFromAsync(seedCatalogue).GetAwaiter().GetResult();
            }

            if (!migrateContext.PublishedCatalogues.Any(c => c.IsCurrent))
            {
                // Seed the model states (the primary FJORD model released, the FJORD-STUDIO clone
                // left as a Draft to demonstrate release from the studio), then republish so the
                // planner's first published catalogue already honours the release gate instead of
                // exposing every authoring model. This is the same store-sourced path every later
                // republish takes.
                seedCatalogue ??= SeedCatalogue.Load();
                foreach (var model in seedCatalogue.Models)
                {
                    if (!migrateContext.ModelStates.Any(s => s.ModelCode == model.Code))
                    {
                        migrateContext.ModelStates.Add(new ModelStateRecord
                        {
                            ModelCode = model.Code,
                            State = model.Code == "FJORD" ? TradeItemState.Active : TradeItemState.Draft,
                        });
                    }
                }
                migrateContext.SaveChanges();

                var publishService = scope.ServiceProvider.GetRequiredService<ModelPublishService>();
                publishService.RepublishAsync().GetAwaiter().GetResult();
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
        builder.Services.AddScoped<ModelPublishService>();
        builder.Services.AddScoped<PriceVersionService>();
        builder.Services.AddScoped<ModelAuthoringService>();
        builder.Services.AddScoped<ArticleAuthoringService>();
        builder.Services.AddScoped<VariantNamingAbsorber>();
        builder.Services.AddScoped<ElementAuthoringService>();
        builder.Services.AddScoped<OptionAuthoringService>();
        builder.Services.AddScoped<BomAuthoringService>();
        builder.Services.AddScoped<MasterAuthoringService>();
        builder.Services.AddScoped<ProductionIdentityService>();
        builder.Services.AddScoped<AuthoringCatalogueStore>();
        builder.Services.AddScoped<PartyService>();
        builder.Services.AddScoped<DiscountService>();
        builder.Services.AddScoped<PinnedCatalogueProvider>();
        builder.Services.AddScoped<OrderEntryService>();

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
