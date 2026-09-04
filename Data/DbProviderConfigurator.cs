using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CheapFurniturePlanner.Data;

/// <summary>
/// Picks the EF Core provider from configuration. Desktop default stays SQLite so an app with
/// no appsettings.json edits - the only case today - behaves exactly as before; PostgreSQL is
/// an opt-in for the future hosted mode (MB-1).
/// </summary>
public static class DbProviderConfigurator
{
    public const string PostgresMigrationsAssembly = "CheapFurniturePlanner.Migrations.Postgres";

    public static void Configure(DbContextOptionsBuilder optionsBuilder, IConfiguration configuration, string defaultSqliteConnectionString)
    {
        var provider = configuration["Database:Provider"];

        if (string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required when Database:Provider is 'postgres'.");
            optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
        }
        else
        {
            // Absent, "sqlite", or any other casing all fall here - matches today's behavior.
            var connectionString = configuration.GetConnectionString("Sqlite") ?? defaultSqliteConnectionString;
            optionsBuilder.UseSqlite(connectionString);
        }
    }
}
