using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

// MB-1 Task 2: proves the column-type pin actually lands on the Npgsql model (and stays absent
// from the SQLite one) for every calendar-date property. Options inspection only, same as
// ProviderRegistrationTests - never opens a real connection, so this runs without a Postgres
// server.
public class NpgsqlTimestampPinTests
{
    private const string FakeNpgsqlConnectionString = "Host=localhost;Database=test;Username=test;Password=test";

    private static readonly (Type Entity, string Property)[] CalendarDateProperties =
    [
        (typeof(Invoice), nameof(Invoice.DueDate)),
        (typeof(Order), nameof(Order.PromisedDeliveryDate)),
        (typeof(PublishedCatalogue), nameof(PublishedCatalogue.EffectiveDate)),
        (typeof(SupplierDelivery), nameof(SupplierDelivery.ExpectedDate)),
        (typeof(Trip), nameof(Trip.DepartureDate)),
        (typeof(InternalRepair), nameof(InternalRepair.ExecutionDate)),
    ];

    [Theory]
    [MemberData(nameof(CalendarDatePropertyCases))]
    public void UnderNpgsql_CalendarDateProperty_IsPinnedToTimestampWithoutTimeZone(Type entityType, string propertyName)
    {
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseNpgsql(FakeNpgsqlConnectionString).Options;
        using var ctx = new FurniturePlannerContext(options);

        var property = ctx.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;

        Assert.Equal("timestamp without time zone", property.GetColumnType());
    }

    [Theory]
    [MemberData(nameof(CalendarDatePropertyCases))]
    public void UnderSqlite_CalendarDateProperty_IsUntouched(Type entityType, string propertyName)
    {
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite("Data Source=test-pin-check.db").Options;
        using var ctx = new FurniturePlannerContext(options);

        var property = ctx.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;

        // SQLite has no separate tz-aware DateTime type - the Npgsql-only pin must not leak here.
        Assert.NotEqual("timestamp without time zone", property.GetColumnType());
    }

    [Fact]
    public void UnderNpgsql_AnInstantProperty_KeepsTheDefaultTimestamptzMapping()
    {
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseNpgsql(FakeNpgsqlConnectionString).Options;
        using var ctx = new FurniturePlannerContext(options);

        var property = ctx.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.CreatedAt))!;

        Assert.Equal("timestamp with time zone", property.GetColumnType());
    }

    public static TheoryData<Type, string> CalendarDatePropertyCases()
    {
        var data = new TheoryData<Type, string>();
        foreach (var (entity, property) in CalendarDateProperties) { data.Add(entity, property); }
        return data;
    }
}
