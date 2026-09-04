using System.Reflection;
using CheapFurniturePlanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Data;

// MB-1 Task 2: an EF-mapped DateTime/DateTime? column is either an INSTANT (written from
// DateTime.UtcNow - CreatedAt, SentAt, PlacedAt, ...) or a CALENDAR DATE (a day-precision value a
// person picks, kind-unspecified - a promised delivery day, an expected delivery day). Npgsql maps
// DateTime to timestamptz by default, which only instants want; calendar dates are pinned to
// "timestamp without time zone" under the Npgsql-only branch in FurniturePlannerContext (SQLite is
// unaffected). This test walks the live EF model (mirrors UiConventionsTests' file-walk approach,
// but over the model instead of the file system) so a NEW mapped DateTime property that nobody
// classified fails loudly here instead of silently defaulting to timestamptz and breaking under
// Postgres - see task-2-report.md for the full audit with write-site evidence per property.
//
// RULE: classify by the DateTimeKind the write sites actually produce, never by semantic
// day-ness. A property named like a "day" (DueDate, EffectiveDate) is still an INSTANT if every
// write site carries Kind=Utc - see the review-fixes section of task-2-report.md for the
// Invoice.DueDate / PublishedCatalogue.EffectiveDate correction that this rule caught.
public class DateTimeClassificationTests
{
    // property: written from DateTime.UtcNow at every site, timestamptz fits as-is.
    private static readonly HashSet<(string Entity, string Property)> Instants = new()
    {
        ("FurnitureItem", "CreatedAt"),
        ("FurnitureItem", "UpdatedAt"),
        ("RoomPlan", "CreatedAt"),
        ("RoomPlan", "UpdatedAt"),
        ("PlannerFurnitureItem", "CreatedAt"),
        ("PlannerFurnitureItem", "UpdatedAt"),
        ("PublishedCatalogue", "PublishedAt"),
        ("Order", "CreatedAt"),
        ("Order", "PlacedAt"),
        ("Invoice", "IssuedAt"),
        ("Invoice", "PaidAt"),
        ("Invoice", "ExportedAt"),
        ("CreditNote", "IssuedAt"),
        ("CreditNote", "SettledAt"),
        ("CreditNote", "ExportedAt"),
        ("ServiceTicket", "CreatedAt"),
        ("ServiceTicket", "ResolvedAt"),
        ("ServiceTicketLog", "At"),
        ("ServiceTicketPhoto", "UploadedAt"),
        ("SupplierReport", "ReportedAt"),
        ("ProductionUnit", "ArrivedAt"),
        ("ProductionUnit", "CreatedAt"),
        ("Trip", "DepartedAt"),
        ("Trip", "CompletedAt"),
        ("SupplierOrder", "CreatedAt"),
        ("SupplierOrder", "SentAt"),
        ("SupplierDelivery", "CreatedAt"),
        ("MaterialStock", "UpdatedAt"),
        ("MaterialOrder", "CreatedAt"),
        ("MaterialOrder", "SentAt"),
        ("MaterialMovement", "OccurredAt"),
        // Inherited from CheapUser (CheapHelpers.EF) - FurnitureUser adds nothing of its own. The
        // write site lives inside that library's Identity plumbing, outside this repo, but a
        // "last login" value is an instant by definition, so timestamptz is still the right
        // default and no pin is needed here.
        ("FurnitureUser", "LastLoginDate"),
        // Review fix: reads like a calendar date but every write site carries Kind=Utc -
        // PublishVersionDialog.razor:20 does SpecifyKind(...,Utc) deliberately; the
        // CataloguePublishService.cs fallback is DateTime.UtcNow. Utc-kinded, so it's an instant.
        ("PublishedCatalogue", "EffectiveDate"),
        // Review fix: same shape - InvoicingService.cs derives it as issuedAt.AddDays(30), and
        // issuedAt is DateTime.UtcNow. Production never passes the optional dueDate override, so
        // this is Utc-kinded at every real write site.
        ("Invoice", "DueDate"),
    };

    // property: day-precision, kind-unspecified - pinned to "timestamp without time zone" under
    // the Npgsql branch in FurniturePlannerContext.ConfigureFurnitureEntities.
    private static readonly HashSet<(string Entity, string Property)> CalendarDates = new()
    {
        ("Order", "PromisedDeliveryDate"),
        ("SupplierDelivery", "ExpectedDate"),
        ("Trip", "DepartureDate"),
        ("InternalRepair", "ExecutionDate"),
    };

    private static (FurniturePlannerContext Context, Microsoft.Data.Sqlite.SqliteConnection Connection) NewSqliteContext()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(conn).Options;
        return (new FurniturePlannerContext(options), conn);
    }

    [Fact]
    public void EveryMappedDateTimeProperty_UnderOurModels_IsClassifiedAsInstantOrCalendarDate()
    {
        var (ctx, conn) = NewSqliteContext();
        using var _ = conn;
        using var ctxDispose = ctx;

        var unclassified = new List<string>();
        var doubleClassified = new List<string>();

        foreach (var entityType in ctx.Model.GetEntityTypes())
        {
            // Scope to our own domain model - CheapContext's base (Identity, etc.) entities are
            // owned and mapped by the CheapHelpers.EF library, not by this project's OnModelCreating.
            if (entityType.ClrType.Namespace != "CheapFurniturePlanner.Models") { continue; }

            foreach (var property in entityType.GetProperties())
            {
                var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clr != typeof(DateTime)) { continue; }

                var key = (entityType.ClrType.Name, property.Name);
                var isInstant = Instants.Contains(key);
                var isCalendar = CalendarDates.Contains(key);

                if (isInstant && isCalendar) { doubleClassified.Add($"{key.Item1}.{key.Item2}"); }
                else if (!isInstant && !isCalendar) { unclassified.Add($"{key.Item1}.{key.Item2}"); }
            }
        }

        Assert.True(unclassified.Count == 0,
            $"Unclassified DateTime propert{(unclassified.Count == 1 ? "y" : "ies")} - add to Instants or CalendarDates above and, if calendar, pin its column type under the Npgsql branch: {string.Join(", ", unclassified)}");
        Assert.True(doubleClassified.Count == 0,
            $"Propert{(doubleClassified.Count == 1 ? "y" : "ies")} listed in both classification sets: {string.Join(", ", doubleClassified)}");
    }

    [Fact]
    public void EveryCalendarDateProperty_StillExistsOnItsEntity()
    {
        // Guards the reverse direction: if a calendar-date property gets renamed/removed, the
        // classification list above goes stale silently (the walk above only ever flags NEW
        // unclassified properties, never orphaned entries). Reflection over the CLR types keeps
        // this independent of the SQLite-mapped model walk above.
        var modelsAssembly = typeof(CheapFurniturePlanner.Models.Order).Assembly;
        foreach (var (entityName, propertyName) in CalendarDates)
        {
            var type = modelsAssembly.GetType($"CheapFurniturePlanner.Models.{entityName}")
                ?? throw new InvalidOperationException($"CalendarDates references unknown entity '{entityName}'");
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"CalendarDates references unknown property '{entityName}.{propertyName}'");
            var clr = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            Assert.Equal(typeof(DateTime), clr);
        }
    }
}
