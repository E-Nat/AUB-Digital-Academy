namespace AUBDigitalAcademy.Models.Enums;

/// <summary>
/// Server-controlled lifecycle states for a student's timed quiz attempt.
/// </summary>
public enum QuizAttemptStatus
{
    InProgress = 0,
    Submitted = 1,
    Expired = 2
}
