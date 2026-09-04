using CheapFurniturePlanner.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

// Options inspection only - never opens a connection, so postgres cases run without a real server.
public class ProviderRegistrationTests
{
    private const string DefaultSqliteConnectionString = "Data Source=test-default.db";

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string ProviderNameFor(DbContextOptionsBuilder<FurniturePlannerContext> optionsBuilder)
    {
        // ProviderName is resolved from the registered provider services, not from an open
        // connection, so constructing the context here never touches a real database.
        using var probe = new FurniturePlannerContext(optionsBuilder.Options);
        return probe.Database.ProviderName!;
    }

    [Fact]
    public void Configure_WithSqliteProvider_UsesSqlite()
    {
        var configuration = BuildConfiguration(new() { ["Database:Provider"] = "sqlite" });
        var optionsBuilder = new DbContextOptionsBuilder<FurniturePlannerContext>();

        DbProviderConfigurator.Configure(optionsBuilder, configuration, DefaultSqliteConnectionString);

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", ProviderNameFor(optionsBuilder));
    }

    [Fact]
    public void Configure_WithNoProviderConfigured_DefaultsToSqlite()
    {
        var configuration = BuildConfiguration([]);
        var optionsBuilder = new DbContextOptionsBuilder<FurniturePlannerContext>();

        DbProviderConfigurator.Configure(optionsBuilder, configuration, DefaultSqliteConnectionString);

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", ProviderNameFor(optionsBuilder));
    }

    [Fact]
    public void Configure_WithNoProviderConfigured_UsesTheSuppliedDefaultConnectionString()
    {
        var configuration = BuildConfiguration([]);
        var optionsBuilder = new DbContextOptionsBuilder<FurniturePlannerContext>();

        DbProviderConfigurator.Configure(optionsBuilder, configuration, DefaultSqliteConnectionString);

        var extension = optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().Single();
        Assert.Equal(DefaultSqliteConnectionString, extension.ConnectionString);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("Postgres")]
    [InlineData("POSTGRES")]
    public void Configure_WithPostgresProvider_UsesNpgsql(string providerValue)
    {
        var configuration = BuildConfiguration(new()
        {
            ["Database:Provider"] = providerValue,
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
        });
        var optionsBuilder = new DbContextOptionsBuilder<FurniturePlannerContext>();

        DbProviderConfigurator.Configure(optionsBuilder, configuration, DefaultSqliteConnectionString);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", ProviderNameFor(optionsBuilder));
    }

    [Fact]
    public void Configure_WithPostgresProvider_SetsPostgresMigrationsAssembly()
    {
        var configuration = BuildConfiguration(new()
        {
            ["Database:Provider"] = "postgres",
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
        });
        var optionsBuilder = new DbContextOptionsBuilder<FurniturePlannerContext>();

        DbProviderConfigurator.Configure(optionsBuilder, configuration, DefaultSqliteConnectionString);

        var extension = optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().Single();
        Assert.Equal("CheapFurniturePlanner.Migrations.Postgres", extension.MigrationsAssembly);
    }

    [Fact]
    public void Configure_WithPostgresProvider_AndNoConnectionString_Throws()
    {
        var configuration = BuildConfiguration(new() { ["Database:Provider"] = "postgres" });
        var optionsBuilder = new DbContextOptionsBuilder<FurniturePlannerContext>();

        Assert.Throws<InvalidOperationException>(() =>
            DbProviderConfigurator.Configure(optionsBuilder, configuration, DefaultSqliteConnectionString));
    }
}
