using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AUBDigitalAcademy.Models.Enums;

namespace AUBDigitalAcademy.Models;

public class CourseItem
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public int Position { get; set; }

    public ItemType Type { get; set; }

    public int ChapterId { get; set; }

    [ForeignKey(nameof(ChapterId))]
    public virtual Chapter Chapter { get; set; } = null!;

    // One-to-one navigation (only one will be non-null based on Type)
    public virtual Lesson? Lesson { get; set; }
    public virtual Book? Book { get; set; }
    public virtual Quiz? Quiz { get; set; }
    public virtual Assignment? Assignment { get; set; }
    public virtual ICollection<CourseProgress> Progress { get; set; } = new List<CourseProgress>();
    public virtual ICollection<CourseItemPrerequisite> Prerequisites { get; set; } = new List<CourseItemPrerequisite>();
    public virtual ICollection<LessonDiscussionThread> DiscussionThreads { get; set; } = new List<LessonDiscussionThread>();
}

