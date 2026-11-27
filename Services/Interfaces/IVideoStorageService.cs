using Microsoft.AspNetCore.Http;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface IVideoStorageService
{
    Task<(bool Success, string? VideoUrl, string? ErrorMessage)> SaveVideoFileAsync(IFormFile file);

    (string FullPath, string ContentType)? GetVideoPhysicalPath(string fileName);

    Task<bool> DeleteVideoFileAsync(string? storedPathOrUrl);
}
