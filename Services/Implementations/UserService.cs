using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class UserService : IUserService
{
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UserService> _logger;

    public UserService(
        UserManager<User> userManager,
        RoleManager<IdentityRole> roleManager,
        ApplicationDbContext context,
        ILogger<UserService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<UserListViewModel>> GetUsersAsync(string? role = null, string? searchTerm = null, bool? isActive = null)
    {
        var query = _context.Users.AsNoTracking().AsQueryable();

        // 1. Role Filter
        if (!string.IsNullOrWhiteSpace(role) && !role.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var roleEntity = await _roleManager.FindByNameAsync(role);
            if (roleEntity != null)
            {
                var userIdsInRole = await _context.UserRoles
                    .Where(ur => ur.RoleId == roleEntity.Id)
                    .Select(ur => ur.UserId)
                    .ToListAsync();

                query = query.Where(u => userIdsInRole.Contains(u.Id));
            }
        }

        // 2. Search Term Filter
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var st = searchTerm.Trim().ToLower();
            query = query.Where(u =>
                (u.FullName != null && u.FullName.ToLower().Contains(st)) ||
                (u.Email != null && u.Email.ToLower().Contains(st)) ||
                (u.UniversityId != null && u.UniversityId.ToLower().Contains(st)));
        }

        // 3. Status Filter
        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        var users = await query
            .Include(u => u.TaughtCourses)
            .Include(u => u.AssistedCourses)
            .Include(u => u.Enrollments)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        // Join roles efficiently
        var userRolesList = await (
            from ur in _context.UserRoles
            join r in _context.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, RoleName = r.Name }
        ).ToListAsync();

        var userRoleDict = userRolesList
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName).FirstOrDefault() ?? "Student");

        var result = new List<UserListViewModel>();
        foreach (var u in users)
        {
            var userRole = userRoleDict.TryGetValue(u.Id, out var rName) ? rName : "Student";
            int count = 0;
            string countLabel = "";

            if (userRole == "Teacher")
            {
                count = u.TaughtCourses?.Count ?? 0;
                countLabel = "Courses Taught";
            }
            else if (userRole == "Assistance")
            {
                count = u.AssistedCourses?.Count ?? 0;
                countLabel = "Assisted Courses";
            }
            else if (userRole == "Student")
            {
                count = u.Enrollments?.Count ?? 0;
                countLabel = "Enrollments";
            }
            else
            {
                count = 0;
                countLabel = "System Admin";
            }

            result.Add(new UserListViewModel
            {
                Id = u.Id,
                FullName = u.FullName ?? string.Empty,
                Email = u.Email ?? string.Empty,
                UniversityId = u.UniversityId,
                Role = userRole,
                Avatar = u.Avatar,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                RelevantCount = count,
                RelevantCountLabel = countLabel
            });
        }

        return result;
    }

    public async Task<UserDetailsViewModel?> GetUserByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.TaughtCourses)
                .ThenInclude(c => c.Major)
            .Include(u => u.TaughtCourses)
                .ThenInclude(c => c.Semester)
            .Include(u => u.TaughtCourses)
                .ThenInclude(c => c.Enrollments)
            .Include(u => u.AssistedCourses)
                .ThenInclude(ca => ca.Course)
                    .ThenInclude(c => c.Major)
            .Include(u => u.AssistedCourses)
                .ThenInclude(ca => ca.Course)
                    .ThenInclude(c => c.Semester)
            .Include(u => u.AssistedCourses)
                .ThenInclude(ca => ca.Course)
                    .ThenInclude(c => c.Enrollments)
            .Include(u => u.Enrollments)
                .ThenInclude(e => e.Course)
                    .ThenInclude(c => c.Teacher)
            .Include(u => u.Enrollments)
                .ThenInclude(e => e.Course)
                    .ThenInclude(c => c.Chapters)
                        .ThenInclude(ch => ch.CourseItems)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? "Student";

        var vm = new UserDetailsViewModel
        {
            Id = user.Id,
            FullName = user.FullName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            UniversityId = user.UniversityId,
            Role = primaryRole,
            Avatar = user.Avatar,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };

        if (primaryRole == "Teacher")
        {
            vm.TaughtCourses = user.TaughtCourses.Select(c => new UserCourseSummaryViewModel
            {
                CourseId = c.Id,
                CourseCode = c.Code,
                CourseTitle = c.Title,
                MajorName = c.Major?.Name ?? "General",
                SemesterName = c.Semester?.Name ?? "Term",
                EnrollmentCount = c.Enrollments.Count,
                Status = c.Status.ToString()
            }).ToList();
        }
        else if (primaryRole == "Assistance")
        {
            vm.TaughtCourses = user.AssistedCourses.Where(ca => ca.Course != null).Select(ca => new UserCourseSummaryViewModel
            {
                CourseId = ca.Course.Id,
                CourseCode = ca.Course.Code,
                CourseTitle = ca.Course.Title,
                MajorName = ca.Course.Major?.Name ?? "General",
                SemesterName = ca.Course.Semester?.Name ?? "Term",
                EnrollmentCount = ca.Course.Enrollments.Count,
                Status = ca.Course.Status.ToString()
            }).ToList();
        }
        else if (primaryRole == "Student")
        {
            var progressRecords = await _context.CourseProgresses
                .AsNoTracking()
                .Where(cp => cp.StudentId == user.Id && cp.IsCompleted)
                .ToListAsync();

            vm.EnrolledCourses = user.Enrollments.Select(e =>
            {
                int totalItems = e.Course?.Chapters.Sum(ch => ch.CourseItems.Count) ?? 0;
                var courseItemIds = e.Course?.Chapters.SelectMany(ch => ch.CourseItems.Select(ci => ci.Id)).ToHashSet() ?? new HashSet<int>();
                int completedItems = progressRecords.Count(cp => courseItemIds.Contains(cp.CourseItemId));
                int progPct = totalItems > 0 ? (int)Math.Round((double)completedItems / totalItems * 100) : 0;

                return new UserEnrollmentSummaryViewModel
                {
                    EnrollmentId = e.Id,
                    CourseId = e.CourseId,
                    CourseCode = e.Course?.Code ?? "AUB",
                    CourseTitle = e.Course?.Title ?? "Course",
                    TeacherName = e.Course?.Teacher?.FullName ?? "Faculty",
                    EnrolledAt = e.EnrolledAt,
                    ProgressPercentage = Math.Min(100, Math.Max(0, progPct)),
                    Status = progPct >= 100 ? "Completed" : "Active"
                };
            }).ToList();
        }

        return vm;
    }

    public async Task<EditUserViewModel?> GetUserForEditAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? "Student";

        return new EditUserViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            UniversityId = user.UniversityId,
            Role = primaryRole,
            CurrentRole = primaryRole,
            Avatar = user.Avatar,
            IsActive = user.IsActive
        };
    }

    public async Task<UserOperationResult> CreateUserAsync(CreateUserViewModel model)
    {
        if (model == null) return UserOperationResult.FailureResult("Invalid user data.");

        var trimmedEmail = model.Email.Trim();
        var trimmedUniversityId = string.IsNullOrWhiteSpace(model.UniversityId) ? null : model.UniversityId.Trim();

        // 1. Uniqueness Validations
        if (!await IsEmailUniqueAsync(trimmedEmail))
        {
            return UserOperationResult.FailureResult($"The email address '{trimmedEmail}' is already registered.");
        }

        if (!string.IsNullOrEmpty(trimmedUniversityId) && !await IsUniversityIdUniqueAsync(trimmedUniversityId))
        {
            return UserOperationResult.FailureResult($"The University ID '{trimmedUniversityId}' is already assigned to another user.");
        }

        // 2. Role Validation
        var targetRole = model.Role;
        if (!new[] { "Admin", "Teacher", "Assistance", "Student" }.Contains(targetRole))
        {
            return UserOperationResult.FailureResult($"Invalid role '{targetRole}' specified.");
        }

        if (!await _roleManager.RoleExistsAsync(targetRole))
        {
            await _roleManager.CreateAsync(new IdentityRole(targetRole));
        }

        // 3. Create User Entity
        var user = new User
        {
            UserName = trimmedEmail,
            Email = trimmedEmail,
            FullName = model.FullName.Trim(),
            UniversityId = trimmedUniversityId,
            IsActive = model.IsActive,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.Select(e => e.Description);
            return UserOperationResult.FailureResult("Failed to create user account.", errors);
        }

        // 4. Assign Role
        var roleResult = await _userManager.AddToRoleAsync(user, targetRole);
        if (!roleResult.Succeeded)
        {
            _logger.LogWarning("User created but role assignment failed for {Email}", trimmedEmail);
        }

        _logger.LogInformation("Admin created user {Email} with role {Role}", trimmedEmail, targetRole);
        return UserOperationResult.SuccessResult($"User '{user.FullName}' ({targetRole}) created successfully.");
    }

    public async Task<UserOperationResult> UpdateUserAsync(EditUserViewModel model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.Id))
        {
            return UserOperationResult.FailureResult("Invalid user data.");
        }

        var user = await _userManager.FindByIdAsync(model.Id);
        if (user == null)
        {
            return UserOperationResult.FailureResult("User not found.");
        }

        var trimmedEmail = model.Email.Trim();
        var trimmedUniversityId = string.IsNullOrWhiteSpace(model.UniversityId) ? null : model.UniversityId.Trim();

        // Protected Admin checks
        bool isProtectedAdmin = user.Email == "admin@aub.com" || user.UniversityId == "ADMIN001";

        // 1. Uniqueness Validations
        if (!await IsEmailUniqueAsync(trimmedEmail, user.Id))
        {
            return UserOperationResult.FailureResult($"The email address '{trimmedEmail}' is already in use by another user.");
        }

        if (!string.IsNullOrEmpty(trimmedUniversityId) && !await IsUniversityIdUniqueAsync(trimmedUniversityId, user.Id))
        {
            return UserOperationResult.FailureResult($"The University ID '{trimmedUniversityId}' is already assigned to another user.");
        }

        // 2. Role Validation & Protection
        var targetRole = model.Role;
        if (!new[] { "Admin", "Teacher", "Assistance", "Student" }.Contains(targetRole))
        {
            return UserOperationResult.FailureResult($"Invalid role '{targetRole}' specified.");
        }

        if (isProtectedAdmin && targetRole != "Admin")
        {
            return UserOperationResult.FailureResult("The primary system administrator account cannot be demoted from the Admin role.");
        }

        if (isProtectedAdmin && !model.IsActive)
        {
            return UserOperationResult.FailureResult("The primary system administrator account cannot be deactivated.");
        }

        // 3. Update Properties
        user.FullName = model.FullName.Trim();
        user.UniversityId = trimmedUniversityId;
        user.IsActive = isProtectedAdmin ? true : model.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        // 4. Update Email & UserName via Identity API if changed
        if (!string.Equals(user.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = trimmedEmail;
            user.UserName = trimmedEmail;
            user.NormalizedEmail = _userManager.KeyNormalizer?.NormalizeEmail(trimmedEmail);
            user.NormalizedUserName = _userManager.KeyNormalizer?.NormalizeName(trimmedEmail);
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = updateResult.Errors.Select(e => e.Description);
            return UserOperationResult.FailureResult("Failed to update user details.", errors);
        }

        // 5. Update Role if changed
        var currentRoles = await _userManager.GetRolesAsync(user);
        if (!currentRoles.Contains(targetRole))
        {
            if (!isProtectedAdmin)
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, targetRole);
            }
        }

        _logger.LogInformation("Admin updated user {UserId} ({Email})", user.Id, user.Email);
        return UserOperationResult.SuccessResult($"User '{user.FullName}' updated successfully.");
    }

    public async Task<UserOperationResult> ToggleUserStatusAsync(string id, string currentUserId)
    {
        if (string.IsNullOrWhiteSpace(id)) return UserOperationResult.FailureResult("Invalid user ID.");

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return UserOperationResult.FailureResult("User not found.");

        // Safety Protections
        if (user.Id == currentUserId)
        {
            return UserOperationResult.FailureResult("You cannot deactivate your own administrative account while logged in.");
        }

        if (user.Email == "admin@aub.com" || user.UniversityId == "ADMIN001")
        {
            return UserOperationResult.FailureResult("The primary system administrator account cannot be deactivated.");
        }

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description);
            return UserOperationResult.FailureResult("Failed to toggle user account status.", errors);
        }

        string actionText = user.IsActive ? "activated" : "deactivated";
        _logger.LogInformation("Admin toggled user {UserId} ({Email}) status to {Status}", user.Id, user.Email, actionText);
        return UserOperationResult.SuccessResult($"User '{user.FullName}' has been {actionText}.");
    }

    public async Task<UserOperationResult> ResetPasswordAsync(string id, string newPassword, string currentUserId)
    {
        if (string.IsNullOrWhiteSpace(id)) return UserOperationResult.FailureResult("Invalid user ID.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return UserOperationResult.FailureResult("New password must be at least 6 characters long.");
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return UserOperationResult.FailureResult("User not found.");

        // Generate Identity reset token and apply new password
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, token, newPassword);

        if (!resetResult.Succeeded)
        {
            var errors = resetResult.Errors.Select(e => e.Description);
            return UserOperationResult.FailureResult("Password reset failed due to password policy validation.", errors);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("Admin {CurrentUserId} reset password for user {TargetUserId} ({Email})", currentUserId, user.Id, user.Email);
        return UserOperationResult.SuccessResult($"Password for '{user.FullName}' has been successfully reset.");
    }

    public async Task<bool> IsUniversityIdUniqueAsync(string universityId, string? excludeUserId = null)
    {
        if (string.IsNullOrWhiteSpace(universityId)) return true;
        var uid = universityId.Trim().ToLower();

        return !await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.UniversityId != null && u.UniversityId.ToLower() == uid && (excludeUserId == null || u.Id != excludeUserId));
    }

    public async Task<bool> IsEmailUniqueAsync(string email, string? excludeUserId = null)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var em = email.Trim().ToLower();

        return !await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email != null && u.Email.ToLower() == em && (excludeUserId == null || u.Id != excludeUserId));
    }

    public async Task<UserCountsViewModel> GetUserCountsAsync()
    {
        var totalUsers = await _context.Users.CountAsync();
        var activeUsers = await _context.Users.CountAsync(u => u.IsActive);
        var inactiveUsers = totalUsers - activeUsers;

        var roleCounts = await (
            from ur in _context.UserRoles
            join r in _context.Roles on ur.RoleId equals r.Id
            group ur by r.Name into g
            select new { RoleName = g.Key, Count = g.Count() }
        ).ToListAsync();

        var studentCount = roleCounts.FirstOrDefault(r => r.RoleName == "Student")?.Count ?? 0;
        var teacherCount = roleCounts.FirstOrDefault(r => r.RoleName == "Teacher")?.Count ?? 0;
        var assistantCount = roleCounts.FirstOrDefault(r => r.RoleName == "Assistance")?.Count ?? 0;
        var adminCount = roleCounts.FirstOrDefault(r => r.RoleName == "Admin")?.Count ?? 0;

        return new UserCountsViewModel
        {
            TotalUsers = totalUsers,
            TotalStudents = studentCount,
            TotalTeachers = teacherCount,
            TotalAssistants = assistantCount,
            TotalAdmins = adminCount,
            ActiveUsers = activeUsers,
            InactiveUsers = inactiveUsers
        };
    }
}
