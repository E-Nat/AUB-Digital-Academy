using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class ModuleEditViewModel
{
    [Required(ErrorMessage = "Module ID is required.")]
    public int Id { get; set; }

    [Required(ErrorMessage = "Course ID is required.")]
    public int CourseId { get; set; }

    [Required(ErrorMessage = "Module title is required.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Module title must be between 2 and 200 characters.")]
    [Display(Name = "Module Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Position is required.")]
    [Display(Name = "Order / Position")]
    [Range(1, 1000, ErrorMessage = "Position must be a positive integer.")]
    public int Position { get; set; }
}
