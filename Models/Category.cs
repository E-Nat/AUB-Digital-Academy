using System.ComponentModel.DataAnnotations;

namespace AUBDigitalAcademy.Models;

public class Category
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    // Navigation
    public virtual ICollection<Major> Majors { get; set; } = new List<Major>();
}
