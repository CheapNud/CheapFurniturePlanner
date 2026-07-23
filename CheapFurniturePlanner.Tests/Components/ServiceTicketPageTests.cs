using Bunit;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Pages;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using CheapHelpers.Services.DataExchange.Pdf;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;
using CheapFurniturePlanner.Tests.Services;

namespace CheapFurniturePlanner.Tests.Components;

// Task 7: /service/{Id} shows the ticket core plus the flow-specific section (internal repair or
// supplier report) and photo upload. Harness mirrors ServiceIntakePageTests/ServiceListPageTests,
// additionally registering ServicePhotoStore, UserAdminService, and SupplierReportPdf so the
// mechanic picker and PDF generation button both resolve real dependencies.
public class ServiceTicketPageTests : TestContext
{
    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);
        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private static async Task<(IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection)> NewFactoryAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        await using (var migrateContext = new FurniturePlannerContext(options))
        {
            await migrateContext.Database.MigrateAsync();
            await RoleSeeder.SeedAsync(migrateContext);
        }
        return (new TestDbContextFactory(options), connection);
    }

    private static async Task<int> SeedConsumerAsync(IDbContextFactory<FurniturePlannerContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var consumer = new Consumer { Name = "Jansen" };
        db.Consumers.Add(consumer);
        await db.SaveChangesAsync();
        return consumer.Id;
    }

    private static async Task<string> SeedMechanicAsync(IDbContextFactory<FurniturePlannerContext> factory, string userName)
    {
        var admin = new UserAdminService(factory, new PasswordHasher<FurnitureUser>());
        await admin.CreateAsync(userName, "Mech", "Anic", "secret1", [Roles.Mechanic]);
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync();
    }

    private void ConfigureServices(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser who)
    {
        var photoRoot = Path.Combine(Path.GetTempPath(), "sv1-ticket-tests", Guid.NewGuid().ToString("N"));
        var pdfRoot = Path.Combine(Path.GetTempPath(), "sv1-ticket-pdf-tests", Guid.NewGuid().ToString("N"));
        Services.AddMudServices();
        Services.AddSingleton(factory);
        Services.AddSingleton(who);
        Services.AddSingleton(sp => new ServiceTicketService(factory, who));
        Services.AddSingleton(new ServicePhotoStore(photoRoot));
        Services.AddSingleton(new UserAdminService(factory, new PasswordHasher<FurnitureUser>()));
        Services.AddSingleton(sp => new SupplierReportPdf(factory, new PdfExportService(new PdfTemplateService()), pdfRoot));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public async Task InternalTicket_AssignedMechanic_CanEditExecution()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var mechanicId = await SeedMechanicAsync(factory, "wrench");
        var consumerId = await SeedConsumerAsync(factory);
        var office = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var ticket = await office.CreateTicketAsync(consumerId, null, "seat sags", null, ServiceFlow.Internal, []);
        await office.DispatchAsync(ticket.Id, mechanicId, null);

        ConfigureServices(factory, new FakeCurrentUser(mechanicId, Roles.Mechanic));
        var cut = Render<ServiceTicketPage>(p => p.Add(x => x.Id, ticket.Id));

        cut.WaitForAssertion(() =>
        {
            var saveButton = cut.FindAll("button").First(b => b.TextContent.Contains("Save execution"));
            Assert.False(saveButton.HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task InternalTicket_OtherMechanic_ExecutionDisabled()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var mechanicId = await SeedMechanicAsync(factory, "wrench");
        var otherMechanicId = await SeedMechanicAsync(factory, "wrench2");
        var consumerId = await SeedConsumerAsync(factory);
        var office = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        var ticket = await office.CreateTicketAsync(consumerId, null, "seat sags", null, ServiceFlow.Internal, []);
        await office.DispatchAsync(ticket.Id, mechanicId, null);

        ConfigureServices(factory, new FakeCurrentUser(otherMechanicId, Roles.Mechanic));
        var cut = Render<ServiceTicketPage>(p => p.Add(x => x.Id, ticket.Id));

        cut.WaitForAssertion(() =>
        {
            var saveButton = cut.FindAll("button").First(b => b.TextContent.Contains("Save execution"));
            Assert.True(saveButton.HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task ExternalTicket_ShowsSupplierSection()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var consumerId = await SeedConsumerAsync(factory);
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var seedingService = new ServiceTicketService(factory, office);
        var ticket = await seedingService.CreateTicketAsync(consumerId, null, "lamp flickers", null, ServiceFlow.External, []);

        ConfigureServices(factory, office);
        var cut = Render<ServiceTicketPage>(p => p.Add(x => x.Id, ticket.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Supplier report", cut.Markup);
            Assert.Contains("Generate report", cut.Markup);
        });
    }

    [Fact]
    public async Task Ticket_PhotoFileMissingFromDisk_RendersWithoutThrowing()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var consumerId = await SeedConsumerAsync(factory);
        var office = new FakeCurrentUser("office-1", Roles.Office);
        var seedingService = new ServiceTicketService(factory, office);
        var ticket = await seedingService.CreateTicketAsync(consumerId, null, "chair wobbles", null, ServiceFlow.Internal, []);
        // Stored file name that was never written via ServicePhotoStore.SaveAsync - simulates a
        // photo row whose backing file is missing from disk.
        await seedingService.AddPhotoAsync(ticket.Id, PhotoKind.Before, "does-not-exist.jpg");

        ConfigureServices(factory, office);
        var cut = Render<ServiceTicketPage>(p => p.Add(x => x.Id, ticket.Id));

        cut.WaitForAssertion(() => Assert.Contains(ticket.TicketNumber, cut.Markup));
    }
}
