using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Student Name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Student Name must be between 2 and 100 characters.")]
    [Display(Name = "Student Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Student ID is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Student ID must be between 3 and 20 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "Student ID can only contain letters, numbers, hyphens, and underscores.")]
    [Display(Name = "Student ID")]
    public string UniversityId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
