using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class ResetPasswordViewModel
{
    [Required(ErrorMessage = "Verification code is required.")]
    [StringLength(10, MinimumLength = 4, ErrorMessage = "Verification code must be between 4 and 10 digits.")]
    [RegularExpression(@"^[0-9]+$", ErrorMessage = "Verification code must contain digits only.")]
    [Display(Name = "Telegram Verification Code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "New password is required.")]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your new password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
