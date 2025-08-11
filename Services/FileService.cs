namespace CheapFurniturePlanner.Services;

public class FileService : IFileService
{
    private readonly string _imageBasePath;

    public FileService()
    {
        _imageBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CheapFurniturePlanner", "Images");
        Directory.CreateDirectory(_imageBasePath);
    }

    public async Task<string> SaveImageAsync(Stream imageStream, string fileName)
    {
        var filePath = Path.Combine(_imageBasePath, fileName);

        using var fileStream = File.Create(filePath);
        await imageStream.CopyToAsync(fileStream);

        return fileName;
    }

    public Task<bool> DeleteImageAsync(string fileName)
    {
        var filePath = Path.Combine(_imageBasePath, fileName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public string GetImageUrl(string fileName)
    {
        var filePath = Path.Combine(_imageBasePath, fileName);
        return File.Exists(filePath) ? $"file://{filePath}" : string.Empty;
    }
}