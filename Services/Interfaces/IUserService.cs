using AUBDigitalAcademy.Models.ViewModels;

namespace AUBDigitalAcademy.Services.Interfaces;

public interface IUserService
{
    Task<IEnumerable<UserListViewModel>> GetUsersAsync(string? role = null, string? searchTerm = null, bool? isActive = null);
    Task<UserDetailsViewModel?> GetUserByIdAsync(string id);
    Task<EditUserViewModel?> GetUserForEditAsync(string id);
    Task<UserOperationResult> CreateUserAsync(CreateUserViewModel model);
    Task<UserOperationResult> UpdateUserAsync(EditUserViewModel model);
    Task<UserOperationResult> ToggleUserStatusAsync(string id, string currentUserId);
    Task<UserOperationResult> ResetPasswordAsync(string id, string newPassword, string currentUserId);
    Task<bool> IsUniversityIdUniqueAsync(string universityId, string? excludeUserId = null);
    Task<bool> IsEmailUniqueAsync(string email, string? excludeUserId = null);
    Task<UserCountsViewModel> GetUserCountsAsync();
}
