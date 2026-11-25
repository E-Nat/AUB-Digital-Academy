using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class VerifyTelegramViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    public string UniversityId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Verification code is required.")]
    [StringLength(10, MinimumLength = 4, ErrorMessage = "Verification code must be between 4 and 10 digits.")]
    [RegularExpression(@"^[0-9]+$", ErrorMessage = "Verification code must contain digits only.")]
    [Display(Name = "Verification Code")]
    public string Code { get; set; } = string.Empty;
}
