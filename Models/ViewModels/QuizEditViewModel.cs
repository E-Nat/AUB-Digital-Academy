using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class QuizEditViewModel
{
    [Required(ErrorMessage = "Course item ID is required.")]
    public int CourseItemId { get; set; }

    public int QuizId { get; set; }

    [Required(ErrorMessage = "Parent Module ID is required.")]
    public int ChapterId { get; set; }

    public int CourseId { get; set; }

    [Required(ErrorMessage = "Quiz title is required.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Title must be between 2 and 200 characters.")]
    [Display(Name = "Quiz Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Time limit is required.")]
    [Range(1, 600, ErrorMessage = "Time limit must be between 1 and 600 minutes.")]
    [Display(Name = "Time Limit (Minutes)")]
    public int TimeLimitMinutes { get; set; } = 30;

    [Required(ErrorMessage = "Passing score percentage is required.")]
    [Range(0.0, 100.0, ErrorMessage = "Passing score must be between 0% and 100%.")]
    [Display(Name = "Passing Score (%)")]
    public double PassingScore { get; set; } = 60.0;

    [Display(Name = "Published Status")]
    public bool IsPublished { get; set; }

    [Required(ErrorMessage = "Position is required.")]
    [Display(Name = "Order / Position")]
    [Range(1, 1000, ErrorMessage = "Position must be a positive integer.")]
    public int Position { get; set; }

    public List<QuestionViewModel> Questions { get; set; } = new();
}
