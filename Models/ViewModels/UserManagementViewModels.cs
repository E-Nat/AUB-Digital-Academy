using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class UserOperationResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new List<string>();

    public static UserOperationResult SuccessResult(string message) => new()
    {
        Succeeded = true,
        Message = message
    };

    public static UserOperationResult FailureResult(string message, IEnumerable<string>? errors = null) => new()
    {
        Succeeded = false,
        Message = message,
        Errors = errors?.ToList() ?? new List<string>()
    };
}

public class UserListViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int RelevantCount { get; set; }
    public string RelevantCountLabel { get; set; } = string.Empty;
}

public class UserCountsViewModel
{
    public int TotalUsers { get; set; }
    public int TotalStudents { get; set; }
    public int TotalTeachers { get; set; }
    public int TotalAssistants { get; set; }
    public int TotalAdmins { get; set; }
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }
}

public class CreateUserViewModel
{
    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "University ID cannot exceed 20 characters.")]
    [Display(Name = "University ID")]
    public string? UniversityId { get; set; }

    [Required(ErrorMessage = "Please select a role.")]
    [Display(Name = "Role")]
    public string Role { get; set; } = "Student";

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm the password.")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "Account Status")]
    public bool IsActive { get; set; } = true;
}

public class EditUserViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "University ID cannot exceed 20 characters.")]
    [Display(Name = "University ID")]
    public string? UniversityId { get; set; }

    [Required(ErrorMessage = "Please select a role.")]
    [Display(Name = "Role")]
    public string Role { get; set; } = string.Empty;

    public string CurrentRole { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    [Display(Name = "Account Status")]
    public bool IsActive { get; set; } = true;
}

public class ResetPasswordAdminViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "New password is required.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long.")]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm new password.")]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm New Password")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public class UserDetailsViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<UserCourseSummaryViewModel> TaughtCourses { get; set; } = new();
    public List<UserEnrollmentSummaryViewModel> EnrolledCourses { get; set; } = new();
}

public class UserCourseSummaryViewModel
{
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string MajorName { get; set; } = string.Empty;
    public string SemesterName { get; set; } = string.Empty;
    public int EnrollmentCount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class UserEnrollmentSummaryViewModel
{
    public int EnrollmentId { get; set; }
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public DateTime EnrolledAt { get; set; }
    public int ProgressPercentage { get; set; }
    public string Status { get; set; } = string.Empty;
}
