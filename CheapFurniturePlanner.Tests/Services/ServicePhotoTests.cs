using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

public class ServicePhotoTests
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
        }
        return (new TestDbContextFactory(options), connection);
    }

    [Fact]
    public async Task SaveAndRead_RoundTripsBytes_UnderTicketFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "sv1-photo-tests", Guid.NewGuid().ToString("N"));
        var store = new ServicePhotoStore(root);
        byte[] payload = [1, 2, 3, 4];

        using var upload = new MemoryStream(payload);
        var storedName = await store.SaveAsync(42, "couch.JPG", upload);

        Assert.EndsWith(".jpg", storedName);
        Assert.True(File.Exists(Path.Combine(root, "42", storedName)));
        Assert.Equal(payload, await store.ReadAsync(42, storedName));
    }

    [Fact]
    public async Task Save_RejectsNonImageExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "sv1-photo-tests", Guid.NewGuid().ToString("N"));
        var store = new ServicePhotoStore(root);
        using var upload = new MemoryStream([1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(1, "malware.exe", upload));
    }

    [Fact]
    public void MimeFor_MapsKnownExtensions()
    {
        Assert.Equal("image/jpeg", ServicePhotoStore.MimeFor("a.jpg"));
        Assert.Equal("image/png", ServicePhotoStore.MimeFor("b.PNG"));
        Assert.Equal("image/webp", ServicePhotoStore.MimeFor("c.webp"));
    }

    [Fact]
    public async Task AddPhoto_RecordsRowWithKindAndUser()
    {
        var (factory, conn) = await NewFactoryAsync();
        using var _ = conn;
        var service = new ServiceTicketService(factory, new FakeCurrentUser("office-1", Roles.Office));
        await using (var db = await factory.CreateDbContextAsync()) { db.Consumers.Add(new Consumer { Name = "Jansen" }); await db.SaveChangesAsync(); }
        await using var db2 = await factory.CreateDbContextAsync();
        var consumerId = await db2.Consumers.Select(c => c.Id).FirstAsync();
        var ticket = await service.CreateTicketAsync(consumerId, null, "x", null, ServiceFlow.Undecided, []);

        await service.AddPhotoAsync(ticket.Id, PhotoKind.After, "abc123.jpg");

        var loaded = await service.GetAsync(ticket.Id);
        var photo = Assert.Single(loaded!.Photos);
        Assert.Equal(PhotoKind.After, photo.Kind);
        Assert.Equal("abc123.jpg", photo.FileName);
        Assert.Equal("office-1", photo.UserId);
    }
}
