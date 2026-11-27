using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Student ID or University Email is required.")]
    [StringLength(150, ErrorMessage = "Identifier must not exceed 150 characters.")]
    [Display(Name = "Student ID or University Email")]
    public string Email { get; set; } = string.Empty;
}

