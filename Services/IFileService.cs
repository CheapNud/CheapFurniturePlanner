namespace CheapFurniturePlanner.Services;

// Simple file service for handling file operations
public interface IFileService
{
    Task<string> SaveImageAsync(Stream imageStream, string fileName);
    Task<bool> DeleteImageAsync(string fileName);
    string GetImageUrl(string fileName);
}
