using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class VideoStorageService : IVideoStorageService
{
    private readonly string _storageFolder;
    private readonly ILogger<VideoStorageService> _logger;

    private static readonly string[] AllowedExtensions = { ".mp4", ".webm", ".ogg" };
    private const long MaxFileSize = 500L * 1024 * 1024; // 500 MB

    public VideoStorageService(IWebHostEnvironment environment, ILogger<VideoStorageService> logger)
    {
        _logger = logger;
        _storageFolder = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", "Videos");
        if (!Directory.Exists(_storageFolder))
        {
            Directory.CreateDirectory(_storageFolder);
        }
    }

    public static bool ValidateVideoSignature(Stream stream, string extension)
    {
        if (stream == null || !stream.CanRead) return false;

        var header = new byte[12];
        long originalPos = stream.CanSeek ? stream.Position : 0;
        int bytesRead = stream.Read(header, 0, header.Length);
        if (stream.CanSeek) stream.Position = originalPos;

        if (bytesRead < 8) return false;

        // Block Windows PE ('MZ') and Linux ELF executables
        if ((header[0] == 0x4D && header[1] == 0x5A) ||
            (header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46))
        {
            return false;
        }

        if (extension == ".mp4")
        {
            // ISO Base Media File Format box: bytes 4..7 == 'ftyp'
            return header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70;
        }

        if (extension == ".webm")
        {
            // EBML header: 0x1A 0x45 0xDF 0xA3
            return header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3;
        }

        if (extension == ".ogg")
        {
            // OggS capture pattern: 0x4F 0x67 0x67 0x53
            return header[0] == 0x4F && header[1] == 0x67 && header[2] == 0x67 && header[3] == 0x53;
        }

        return false;
    }

    public async Task<(bool Success, string? VideoUrl, string? ErrorMessage)> SaveVideoFileAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return (false, null, "No video file provided or file is empty.");
        }

        if (file.Length > MaxFileSize)
        {
            return (false, null, "Video file exceeds the 500 MB maximum upload limit.");
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            return (false, null, $"Video format '{ext}' is not supported. Please upload an MP4, WebM, or OGG video.");
        }

        using (var stream = file.OpenReadStream())
        {
            if (!ValidateVideoSignature(stream, ext))
            {
                return (false, null, $"File signature does not match a valid '{ext}' video.");
            }
        }

        var secureFileName = $"{Guid.NewGuid():N}{ext}";
        var destinationPath = Path.Combine(_storageFolder, secureFileName);
        var canonicalPath = Path.GetFullPath(destinationPath);
        if (!canonicalPath.StartsWith(Path.GetFullPath(_storageFolder), StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, "Invalid video storage path.");
        }

        try
        {
            await using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(fs);
            _logger.LogInformation("Saved lesson video file {FileName} ({Size} bytes).", secureFileName, file.Length);
            return (true, $"/Student/Video/{secureFileName}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save lesson video file.");
            return (false, null, "An internal error occurred while saving the video file.");
        }
    }

    public (string FullPath, string ContentType)? GetVideoPhysicalPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var safeFileName = Path.GetFileName(fileName);
        var fullPath = Path.Combine(_storageFolder, safeFileName);
        var canonicalPath = Path.GetFullPath(fullPath);
        if (!canonicalPath.StartsWith(Path.GetFullPath(_storageFolder), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!File.Exists(fullPath)) return null;

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".ogg" => "video/ogg",
            _ => "application/octet-stream"
        };

        return (fullPath, contentType);
    }

    public Task<bool> DeleteVideoFileAsync(string? storedPathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(storedPathOrUrl) ||
            !storedPathOrUrl.StartsWith("/Student/Video/", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        var safeFileName = Path.GetFileName(storedPathOrUrl);
        var fullPath = Path.Combine(_storageFolder, safeFileName);
        var canonicalPath = Path.GetFullPath(fullPath);
        if (!canonicalPath.StartsWith(Path.GetFullPath(_storageFolder), StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        if (!File.Exists(fullPath)) return Task.FromResult(false);

        try
        {
            File.Delete(fullPath);
            _logger.LogInformation("Deleted lesson video file {FileName}.", safeFileName);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete lesson video file {FileName}.", safeFileName);
            return Task.FromResult(false);
        }
    }
}
