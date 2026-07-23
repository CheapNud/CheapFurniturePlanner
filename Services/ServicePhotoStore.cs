namespace CheapFurniturePlanner.Services;

// Photo bytes live on disk under <root>/<ticketId>/<guid><ext>; only the stored file name goes
// in the database. Extension whitelist is the only validation - this is a desktop app behind
// login, not an internet upload endpoint.
public sealed class ServicePhotoStore(string rootPath)
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task<string> SaveAsync(int ticketId, string originalFileName, Stream contentStream, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"'{extension}' is not a supported image type.");
        }
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var ticketFolder = Path.Combine(rootPath, ticketId.ToString());
        Directory.CreateDirectory(ticketFolder);
        await using var target = File.Create(Path.Combine(ticketFolder, storedName));
        await contentStream.CopyToAsync(target, ct);
        return storedName;
    }

    public async Task<byte[]> ReadAsync(int ticketId, string fileName, CancellationToken ct = default) =>
        await File.ReadAllBytesAsync(Path.Combine(rootPath, ticketId.ToString(), fileName), ct);

    public static string MimeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}
