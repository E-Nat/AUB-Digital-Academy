using System.ComponentModel.DataAnnotations.Schema;

namespace AUBDigitalAcademy.Models;

public class CourseItemPrerequisite
{
    public int Id { get; set; }

    public int CourseItemId { get; set; }

    [ForeignKey(nameof(CourseItemId))]
    public virtual CourseItem CourseItem { get; set; } = null!;

    public int RequiredCourseItemId { get; set; }

    [ForeignKey(nameof(RequiredCourseItemId))]
    public virtual CourseItem RequiredCourseItem { get; set; } = null!;
}
