using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models;

public class Semester
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    public bool IsCurrent { get; set; }

    public virtual ICollection<Course> Courses { get; set; } = new List<Course>();
}
