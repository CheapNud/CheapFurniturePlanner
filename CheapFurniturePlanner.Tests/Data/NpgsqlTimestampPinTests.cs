using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

// MB-1 Task 2: proves the column-type pin actually lands on the Npgsql model (and stays absent
// from the SQLite one) for every calendar-date property. Options inspection only, same as
// ProviderRegistrationTests - never opens a real connection, so this runs without a Postgres
// server.
//
// RULE: classify by the DateTimeKind the write sites actually produce, never by semantic
// day-ness. A Utc-kinded day-precision value is an instant, not a calendar date - pinning it to
// "timestamp without time zone" throws under Npgsql instead of fixing anything.
public class NpgsqlTimestampPinTests
{
    private const string FakeNpgsqlConnectionString = "Host=localhost;Database=test;Username=test;Password=test";

    private static readonly (Type Entity, string Property)[] CalendarDateProperties =
    [
        (typeof(Order), nameof(Order.PromisedDeliveryDate)),
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

    // Review fix: PublishedCatalogue.EffectiveDate and Invoice.DueDate look like calendar dates but
    // are Utc-kinded at every write site, so they were moved off the pinned list above and must
    // keep the default timestamptz mapping like any other instant - see the DateTime
    // classification block in FurniturePlannerContext.OnModelCreating for the write-site evidence.
    private static readonly (Type Entity, string Property)[] InstantProperties =
    [
        (typeof(Order), nameof(Order.CreatedAt)),
        (typeof(PublishedCatalogue), nameof(PublishedCatalogue.EffectiveDate)),
        (typeof(Invoice), nameof(Invoice.DueDate)),
    ];

    [Theory]
    [MemberData(nameof(InstantPropertyCases))]
    public void UnderNpgsql_AnInstantProperty_KeepsTheDefaultTimestamptzMapping(Type entityType, string propertyName)
    {
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseNpgsql(FakeNpgsqlConnectionString).Options;
        using var ctx = new FurniturePlannerContext(options);

        var property = ctx.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;

        Assert.Equal("timestamp with time zone", property.GetColumnType());
    }

    public static TheoryData<Type, string> InstantPropertyCases()
    {
        var data = new TheoryData<Type, string>();
        foreach (var (entity, property) in InstantProperties) { data.Add(entity, property); }
        return data;
    }

    public static TheoryData<Type, string> CalendarDatePropertyCases()
    {
        var data = new TheoryData<Type, string>();
        foreach (var (entity, property) in CalendarDateProperties) { data.Add(entity, property); }
        return data;
    }
}
