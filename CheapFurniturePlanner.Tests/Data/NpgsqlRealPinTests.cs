using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

// MB-1 review fix: the pre-existing SQLite REAL pins (HasColumnType("REAL") calls and the
// PlannerFurnitureItem.CachedUnitPrice [Column(TypeName="REAL")] attribute) flowed unbranched into
// Postgres, landing as float4 - a decimal money column (Price, CachedUnitPrice) squeezed into
// binary float, and dimension/coordinate doubles losing SQLite's REAL affinity for a narrower
// 32-bit type than Npgsql's own double-mapping default. Same provider-branch pattern as the
// timestamp pin in NpgsqlTimestampPinTests: Options inspection only, no real connection needed.
public class NpgsqlRealPinTests
{
    private const string FakeNpgsqlConnectionString = "Host=localhost;Database=test;Username=test;Password=test";

    [Theory]
    [InlineData(typeof(FurnitureItem), nameof(FurnitureItem.Price))]
    [InlineData(typeof(PlannerFurnitureItem), nameof(PlannerFurnitureItem.CachedUnitPrice))]
    public void UnderNpgsql_DecimalMoneyProperty_MapsToNumeric(Type entityType, string propertyName)
    {
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseNpgsql(FakeNpgsqlConnectionString).Options;
        using var ctx = new FurniturePlannerContext(options);

        var property = ctx.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;

        Assert.Equal("numeric", property.GetColumnType());
    }

    [Fact]
    public void UnderNpgsql_DoubleDimensionProperty_MapsToDoublePrecision()
    {
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseNpgsql(FakeNpgsqlConnectionString).Options;
        using var ctx = new FurniturePlannerContext(options);

        var property = ctx.Model.FindEntityType(typeof(FurnitureItem))!.FindProperty(nameof(FurnitureItem.Width))!;

        Assert.Equal("double precision", property.GetColumnType());
    }

    [Fact]
    public void UnderSqlite_DecimalMoneyProperty_StaysReal()
    {
        // Byte-identical desktop proof: the SQLite chain must not move under this fix.
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite("Data Source=test-real-pin-check.db").Options;
        using var ctx = new FurniturePlannerContext(options);

        var property = ctx.Model.FindEntityType(typeof(FurnitureItem))!.FindProperty(nameof(FurnitureItem.Price))!;

        Assert.Equal("REAL", property.GetColumnType());
    }
}
