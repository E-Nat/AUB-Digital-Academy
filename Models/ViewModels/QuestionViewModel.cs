using System.ComponentModel.DataAnnotations;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models.ViewModels;

public class QuestionViewModel
{
    public int Id { get; set; }

    public int QuizId { get; set; }

    [Required(ErrorMessage = "Question text is required.")]
    [StringLength(1000, MinimumLength = 3, ErrorMessage = "Question text must be between 3 and 1000 characters.")]
    [Display(Name = "Question Text")]
    public string Text { get; set; } = string.Empty;

    [Display(Name = "Question Type")]
    public QuestionType Type { get; set; } = QuestionType.MultipleChoice;

    [Range(0.1, 100.0, ErrorMessage = "Points must be between 0.1 and 100.")]
    [Display(Name = "Points")]
    public double Points { get; set; } = 1.0;

    [Display(Name = "Sort Order")]
    public int Order { get; set; }

    public List<QuestionOptionViewModel> Options { get; set; } = new();
}

public class QuestionOptionViewModel
{
    public int Id { get; set; }

    public int QuestionId { get; set; }

    [Required(ErrorMessage = "Option text is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Option text must be between 1 and 500 characters.")]
    [Display(Name = "Option Text")]
    public string Text { get; set; } = string.Empty;

    [Display(Name = "Correct Answer")]
    public bool IsCorrect { get; set; }
}

public class QuizQuestionsViewModel
{
    public int QuizId { get; set; }
    public int CourseItemId { get; set; }
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string QuizTitle { get; set; } = string.Empty;
    public int ChapterId { get; set; }
    public string ChapterTitle { get; set; } = string.Empty;
    public int TimeLimitMinutes { get; set; }
    public double PassingScore { get; set; }
    public bool IsPublished { get; set; }
    public List<QuestionViewModel> Questions { get; set; } = new();
    public int TotalQuestions => Questions.Count;
    public double TotalPoints => Questions.Sum(q => q.Points);
    public QuestionViewModel NewQuestion { get; set; } = new();
}
