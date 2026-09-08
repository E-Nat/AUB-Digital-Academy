using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class AssignmentCreateViewModel
{
    [Required(ErrorMessage = "Parent Module ID is required.")]
    public int ChapterId { get; set; }

    public int CourseId { get; set; }

    [Required(ErrorMessage = "Assignment title is required.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Title must be between 2 and 200 characters.")]
    [Display(Name = "Assignment Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Instructions are required.")]
    [Display(Name = "Instructions / Task Description")]
    public string Instruction { get; set; } = string.Empty;

    [Required(ErrorMessage = "Maximum score is required.")]
    [Range(1.0, 1000.0, ErrorMessage = "Maximum score must be between 1 and 1000.")]
    [Display(Name = "Maximum Score")]
    public double MaxScore { get; set; } = 100.0;

    [Required(ErrorMessage = "Due date is required.")]
    [Display(Name = "Due Date & Time")]
    [DataType(DataType.DateTime)]
    public DateTime DueDate { get; set; } = DateTime.UtcNow.AddDays(7);

    [Display(Name = "Order / Position")]
    [Range(1, 1000, ErrorMessage = "Position must be a positive integer.")]
    public int? Position { get; set; }
}
