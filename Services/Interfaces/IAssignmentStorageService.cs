using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface IAssignmentStorageService
{
    Task<(bool Success, string? StoredPath, string? OriginalName, long? FileSize, string? ContentType, string? ErrorMessage)> SaveAssignmentFileAsync(
        IFormFile file, string studentId, int assignmentId);

    Task<(byte[]? FileBytes, string ContentType, string DownloadFileName)?> GetAssignmentFileAsync(
        string storedPath, string? originalFileName);
}
