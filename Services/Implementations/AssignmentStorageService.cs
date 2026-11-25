using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class AssignmentStorageService : IAssignmentStorageService
{
    private readonly string _storageFolder;
    private readonly ILogger<AssignmentStorageService> _logger;

    private static readonly string[] AllowedExtensions = { ".pdf", ".zip", ".docx" };
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public AssignmentStorageService(IWebHostEnvironment environment, ILogger<AssignmentStorageService> logger)
    {
        _logger = logger;
        // Store outside wwwroot to prevent direct unauthenticated HTTP access
        _storageFolder = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", "Assignments");
        if (!Directory.Exists(_storageFolder))
        {
            Directory.CreateDirectory(_storageFolder);
        }
    }

    public async Task<(bool Success, string? StoredPath, string? OriginalName, long? FileSize, string? ContentType, string? ErrorMessage)> SaveAssignmentFileAsync(
        IFormFile file, string studentId, int assignmentId)
    {
        if (file == null || file.Length == 0)
        {
            return (false, null, null, null, null, "No file provided or file is empty.");
        }

        if (file.Length > MaxFileSize)
        {
            return (false, null, null, null, null, $"File size exceeds the 10 MB maximum limit (size: {Math.Round((double)file.Length / 1024 / 1024, 2)} MB).");
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            return (false, null, null, null, null, $"File format '{ext}' is not permitted. Only PDF, ZIP, and DOCX files are allowed.");
        }

        // Sanitize original file name for safe metadata storage
        var originalFileName = Path.GetFileName(file.FileName);
        if (originalFileName.Length > 255)
        {
            originalFileName = originalFileName.Substring(0, 250) + ext;
        }

        // Generate GUID-based storage name to prevent collisions and path traversal
        var secureFileName = $"{Guid.NewGuid():N}{ext}";
        var destinationPath = Path.Combine(_storageFolder, secureFileName);

        // Path traversal guard
        var canonicalPath = Path.GetFullPath(destinationPath);
        if (!canonicalPath.StartsWith(Path.GetFullPath(_storageFolder), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Potential path traversal attempt detected for student {StudentId}", studentId);
            return (false, null, null, null, null, "Invalid file path.");
        }

        try
        {
            await using var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(stream);

            _logger.LogInformation("Assignment file saved successfully: {SecureName} for Student {StudentId}, Assignment {AssignmentId}",
                secureFileName, studentId, assignmentId);

            var relativeStoredPath = secureFileName;
            return (true, relativeStoredPath, originalFileName, file.Length, file.ContentType, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write assignment file to disk for Student {StudentId}", studentId);
            return (false, null, null, null, null, "An internal error occurred while saving the file.");
        }
    }

    public async Task<(byte[]? FileBytes, string ContentType, string DownloadFileName)?> GetAssignmentFileAsync(
        string storedPath, string? originalFileName)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return null;
        }

        // In storedPath we only expect the GUID filename
        var safeFileName = Path.GetFileName(storedPath);
        var fullPath = Path.Combine(_storageFolder, safeFileName);

        var canonicalPath = Path.GetFullPath(fullPath);
        if (!canonicalPath.StartsWith(Path.GetFullPath(_storageFolder), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal attempt blocked in GetAssignmentFileAsync: {Path}", storedPath);
            return null;
        }

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Assignment file not found on disk: {Path}", fullPath);
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(fullPath);
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _ => "application/octet-stream"
            };

            var downloadName = !string.IsNullOrWhiteSpace(originalFileName)
                ? Path.GetFileName(originalFileName)
                : safeFileName;

            return (bytes, contentType, downloadName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading assignment file from disk: {Path}", fullPath);
            return null;
        }
    }
}
