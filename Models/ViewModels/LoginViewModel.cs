using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "University ID or Email is required.")]
    [Display(Name = "University ID or Email")]
    public string LoginId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}

