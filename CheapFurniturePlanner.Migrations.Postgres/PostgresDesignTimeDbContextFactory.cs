using CheapFurniturePlanner.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CheapFurniturePlanner.Migrations.Postgres;

/// <summary>
/// Design-time factory for the Postgres migrations chain. EF tooling picks this up when this
/// project is both --project and --startup-project, so the Postgres baseline can be generated
/// without touching the app project's own (SQLite) design-time factory.
///
/// The dummy connection string is never opened - migrations add/remove/script are pure code-gen
/// against the model, no database connection is made. UseNpgsql is what matters here: the app's
/// OnModelCreating branches on Database.IsNpgsql(), so picking this provider at design time is
/// what drives the Postgres-specific index/column shapes into the generated migration.
/// </summary>
public class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FurniturePlannerContext>
{
    public FurniturePlannerContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>()
            .UseNpgsql("Host=localhost;Database=design", npgsql => npgsql.MigrationsAssembly(DbProviderConfigurator.PostgresMigrationsAssembly))
            .Options;
        return new FurniturePlannerContext(options);
    }
}
