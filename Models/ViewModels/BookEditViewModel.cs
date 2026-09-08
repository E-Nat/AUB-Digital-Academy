using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models.ViewModels;

public class BookEditViewModel
{
    [Required(ErrorMessage = "Course item ID is required.")]
    public int CourseItemId { get; set; }

    public int BookId { get; set; }

    [Required(ErrorMessage = "Parent Module ID is required.")]
    public int ChapterId { get; set; }

    public int CourseId { get; set; }

    [Required(ErrorMessage = "Resource / Book title is required.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Title must be between 2 and 200 characters.")]
    [Display(Name = "Resource Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Document file URL or path is required.")]
    [StringLength(500, ErrorMessage = "File URL cannot exceed 500 characters.")]
    [Display(Name = "File URL / Path")]
    public string FileUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "File type format is required (e.g. PDF, EPUB, DOCX).")]
    [StringLength(20, ErrorMessage = "File type cannot exceed 20 characters.")]
    [Display(Name = "File Type")]
    public string FileType { get; set; } = "PDF";

    [Display(Name = "File Size (bytes)")]
    [Range(0, long.MaxValue, ErrorMessage = "File size must be positive.")]
    public long? FileSize { get; set; }

    [Required(ErrorMessage = "Position is required.")]
    [Display(Name = "Order / Position")]
    [Range(1, 1000, ErrorMessage = "Position must be a positive integer.")]
    public int Position { get; set; }
}
