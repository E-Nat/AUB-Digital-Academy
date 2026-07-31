using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models;

public class ApplicationUser : IdentityUser
{
    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(20)]
    public string? UniversityId { get; set; }

    public string? Avatar { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<Course> TaughtCourses { get; set; } = new List<Course>();
    public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public virtual ICollection<QuizSubmission> QuizSubmissions { get; set; } = new List<QuizSubmission>();
    public virtual ICollection<AssignmentSubmission> AssignmentSubmissions { get; set; } = new List<AssignmentSubmission>();
    public virtual ICollection<AssignmentSubmission> GradedSubmissions { get; set; } = new List<AssignmentSubmission>();
}
