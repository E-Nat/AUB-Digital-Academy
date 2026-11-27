using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace AUBDigitalAcademy.Models.ViewModels;

public class LessonEditViewModel
{
    [Required(ErrorMessage = "Course item ID is required.")]
    public int CourseItemId { get; set; }

    public int LessonId { get; set; }

    [Required(ErrorMessage = "Module ID is required.")]
    public int ChapterId { get; set; }

    public int CourseId { get; set; }

    [Required(ErrorMessage = "Lesson title is required.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Lesson title must be between 2 and 200 characters.")]
    [Display(Name = "Lesson Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Video URL")]
    [StringLength(500, ErrorMessage = "Video URL cannot exceed 500 characters.")]
    public string? VideoUrl { get; set; }

    [Display(Name = "Upload Replacement Video")]
    public IFormFile? VideoFile { get; set; }

    public bool RemoveVideo { get; set; }

    [Display(Name = "Lesson Content (HTML / Text)")]
    public string? ContentHtml { get; set; }

    [Required(ErrorMessage = "Position is required.")]
    [Display(Name = "Order / Position")]
    [Range(1, 1000, ErrorMessage = "Position must be a positive integer.")]
    public int Position { get; set; }

    public List<int> PrerequisiteItemIds { get; set; } = new();
}
