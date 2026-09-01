using System.ComponentModel.DataAnnotations;
using AUBDigitalAcademy.Models.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AUBDigitalAcademy.Models.ViewModels;

public class CourseEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Course code is required.")]
    [StringLength(20, ErrorMessage = "Course code cannot exceed 20 characters.")]
    [Display(Name = "Course Code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Course title is required.")]
    [StringLength(200, ErrorMessage = "Course title cannot exceed 200 characters.")]
    [Display(Name = "Course Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Cover Image URL / Path")]
    public string? Thumbnail { get; set; }

    [Display(Name = "Status")]
    public CourseStatus Status { get; set; } = CourseStatus.Draft;

    [Required(ErrorMessage = "Please select a major.")]
    [Display(Name = "Major")]
    public int MajorId { get; set; }

    [Required(ErrorMessage = "Please select a semester.")]
    [Display(Name = "Semester")]
    public int SemesterId { get; set; }

    [Required(ErrorMessage = "Please select a teacher.")]
    [Display(Name = "Teacher")]
    public string TeacherId { get; set; } = string.Empty;

    [Display(Name = "Category")]
    public int? CategoryId { get; set; }

    // Dropdown list data for UI
    public List<SelectListItem> MajorOptions { get; set; } = new();
    public List<SelectListItem> SemesterOptions { get; set; } = new();
    public List<SelectListItem> TeacherOptions { get; set; } = new();
    public List<SelectListItem> CategoryOptions { get; set; } = new();
}
