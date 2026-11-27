using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

    private static readonly Dictionary<string, string[]> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".pdf", new[] { "application/pdf" } },
        { ".zip", new[] { "application/zip", "application/x-zip-compressed", "application/octet-stream", "multipart/x-zip" } },
        { ".docx", new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/zip", "application/octet-stream" } }
    };

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

    public static bool ValidateFileSignature(Stream stream, string extension)
    {
        if (stream == null || !stream.CanRead) return false;

        var header = new byte[8];
        long originalPos = stream.CanSeek ? stream.Position : 0;
        int bytesRead = stream.Read(header, 0, header.Length);
        if (stream.CanSeek) stream.Position = originalPos;

        if (bytesRead < 4) return false;

        // Block executable headers ('MZ' = 0x4D 0x5A)
        if (header[0] == 0x4D && header[1] == 0x5A)
        {
            return false;
        }

        // Block Linux ELF executables (0x7F 'E' 'L' 'F')
        if (header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46)
        {
            return false;
        }

        if (extension == ".pdf")
        {
            // PDF starts with %PDF (0x25, 0x50, 0x44, 0x46)
            return header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;
        }

        if (extension == ".zip" || extension == ".docx")
        {
            // Standard PK ZIP header: 0x50 0x4B 0x03 0x04 or 0x50 0x4B 0x05 0x06
            bool isZip = (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04) ||
                         (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x05 && header[3] == 0x06);
            if (!isZip) return false;

            if (extension == ".docx")
            {
                // Verify it is a valid OpenXML Word document package
                try
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                    bool hasContentTypes = archive.GetEntry("[Content_Types].xml") != null;
                    bool hasWord = archive.Entries.Any(e => e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase));
                    if (stream.CanSeek) stream.Position = originalPos;
                    return hasContentTypes && hasWord;
                }
                catch
                {
                    if (stream.CanSeek) stream.Position = originalPos;
                    return false;
                }
            }

            return true;
        }

        return false;
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

        // Validate MIME type if supplied
        if (!string.IsNullOrWhiteSpace(file.ContentType) && AllowedMimeTypes.TryGetValue(ext, out var allowedMimes))
        {
            if (!allowedMimes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                return (false, null, null, null, null, $"MIME type '{file.ContentType}' does not match the expected format for '{ext}' files.");
            }
        }

        // Validate file signature / magic bytes
        using (var stream = file.OpenReadStream())
        {
            if (!ValidateFileSignature(stream, ext))
            {
                return (false, null, null, null, null, $"File content signature does not match valid '{ext}' specification. Disallowed or renamed executable files are rejected.");
            }
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
            var effectiveContentType = ext switch
            {
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _ => file.ContentType ?? "application/octet-stream"
            };

            return (true, relativeStoredPath, originalFileName, file.Length, effectiveContentType, null);
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

    public Task<bool> DeleteAssignmentFileAsync(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return Task.FromResult(false);
        }

        var safeFileName = Path.GetFileName(storedPath);
        var fullPath = Path.Combine(_storageFolder, safeFileName);

        var canonicalPath = Path.GetFullPath(fullPath);
        if (!canonicalPath.StartsWith(Path.GetFullPath(_storageFolder), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal attempt blocked in DeleteAssignmentFileAsync: {Path}", storedPath);
            return Task.FromResult(false);
        }

        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            File.Delete(fullPath);
            _logger.LogInformation("Assignment file deleted during cleanup: {Path}", safeFileName);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting assignment file during cleanup: {Path}", safeFileName);
            return Task.FromResult(false);
        }
    }
}
