using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Services.Implementations;

public class TeacherService : ITeacherService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TeacherService> _logger;
    private readonly IHtmlSanitizationService _htmlSanitizer;

    public TeacherService(
        ApplicationDbContext context,
        ILogger<TeacherService> logger,
        IHtmlSanitizationService? htmlSanitizer = null)
    {
        _context = context;
        _logger = logger;
        _htmlSanitizer = htmlSanitizer ?? new HtmlSanitizationService();
    }

    public async Task<List<int>> GetAuthorizedCourseIdsAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return new List<int>();

        var taughtCourseIds = await _context.Courses
            .AsNoTracking()
            .Where(c => c.TeacherId == userId)
            .Select(c => c.Id)
            .ToListAsync();

        var assistedCourseIds = await _context.CourseAssistants
            .AsNoTracking()
            .Where(ca => ca.AssistantId == userId)
            .Select(ca => ca.CourseId)
            .ToListAsync();

        return taughtCourseIds.Union(assistedCourseIds).Distinct().ToList();
    }

    public async Task<bool> HasAccessToCourseAsync(int courseId, string userId)
    {
        if (courseId <= 0 || string.IsNullOrWhiteSpace(userId)) return false;

        var isTeacher = await _context.Courses
            .AsNoTracking()
            .AnyAsync(c => c.Id == courseId && c.TeacherId == userId);

        if (isTeacher) return true;

        var isAssistant = await _context.CourseAssistants
            .AsNoTracking()
            .AnyAsync(ca => ca.CourseId == courseId && ca.AssistantId == userId);

        return isAssistant;
    }

    public async Task<bool> IsAssistantAssignedToCourseAsync(int courseId, string assistantId)
    {
        if (courseId <= 0 || string.IsNullOrWhiteSpace(assistantId)) return false;

        return await _context.CourseAssistants
            .AsNoTracking()
            .AnyAsync(ca => ca.CourseId == courseId && ca.AssistantId == assistantId);
    }

    public async Task<bool> CanGradeCourseAsync(int courseId, string userId)
    {
        if (courseId <= 0 || string.IsNullOrWhiteSpace(userId)) return false;

        var isTeacher = await _context.Courses
            .AsNoTracking()
            .AnyAsync(c => c.Id == courseId && c.TeacherId == userId);

        if (isTeacher) return true;

        var assistantRecord = await _context.CourseAssistants
            .AsNoTracking()
            .FirstOrDefaultAsync(ca => ca.CourseId == courseId && ca.AssistantId == userId);

        return assistantRecord != null && assistantRecord.CanGrade;
    }

    public async Task<bool> CanGradeSubmissionAsync(int submissionId, string userId)
    {
        if (submissionId <= 0 || string.IsNullOrWhiteSpace(userId)) return false;

        var submission = await _context.AssignmentSubmissions
            .AsNoTracking()
            .Include(s => s.Assignment)
                .ThenInclude(a => a.CourseItem)
                    .ThenInclude(ci => ci.Chapter)
            .FirstOrDefaultAsync(s => s.Id == submissionId);

        if (submission?.Assignment?.CourseItem?.Chapter?.CourseId is int courseId && courseId > 0)
        {
            return await CanGradeCourseAsync(courseId, userId);
        }

        return false;
    }

    public async Task<TeacherDashboardViewModel> GetTeacherDashboardAsync(string teacherId)
    {
        var viewModel = new TeacherDashboardViewModel();

        try
        {
            var teacherCourseIds = await GetAuthorizedCourseIdsAsync(teacherId);

            if (!teacherCourseIds.Any())
            {
                return viewModel;
            }

            // Quick Stats
            viewModel.ActiveCoursesCount = teacherCourseIds.Count;

            viewModel.TotalStudentsCount = await _context.Enrollments
                .AsNoTracking()
                .Where(e => teacherCourseIds.Contains(e.CourseId))
                .Select(e => e.StudentId)
                .Distinct()
                .CountAsync();

            var latestPendingSubmissionsQuery = _context.AssignmentSubmissions
                .AsNoTracking()
                .Where(s => teacherCourseIds.Contains(s.Assignment.CourseItem.Chapter.CourseId) &&
                            !_context.AssignmentSubmissions.Any(newer =>
                                newer.AssignmentId == s.AssignmentId &&
                                newer.StudentId == s.StudentId &&
                                (newer.SubmittedAt > s.SubmittedAt ||
                                 (newer.SubmittedAt == s.SubmittedAt && newer.Id > s.Id))))
                .Where(s => s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.Late);

            viewModel.PendingReviewsCount = await latestPendingSubmissionsQuery.CountAsync();

            viewModel.ActiveQuizzesCount = await _context.Quizzes
                .AsNoTracking()
                .Where(q => teacherCourseIds.Contains(q.CourseItem.Chapter.CourseId) && q.IsPublished)
                .CountAsync();

            // Attention Items (Pending assignment reviews)
            var pendingSubmissionsGrouped = await latestPendingSubmissionsQuery
                .GroupBy(s => s.AssignmentId)
                .Select(g => new
                {
                    AssignmentId = g.Key,
                    PendingCount = g.Count()
                })
                .ToListAsync();

            if (pendingSubmissionsGrouped.Any())
            {
                var pendingAssignmentIds = pendingSubmissionsGrouped.Select(p => p.AssignmentId).ToList();
                var assignmentsInfo = await _context.Assignments
                    .AsNoTracking()
                    .Where(a => pendingAssignmentIds.Contains(a.Id))
                    .Select(a => new
                    {
                        a.Id,
                        a.CourseItem.Title,
                        CourseName = a.CourseItem.Chapter.Course.Title,
                        a.DueDate
                    })
                    .ToListAsync();

                viewModel.AttentionItems = pendingSubmissionsGrouped.Select(p =>
                {
                    var info = assignmentsInfo.FirstOrDefault(a => a.Id == p.AssignmentId);
                    var isPastDue = info != null && DateTime.UtcNow > info.DueDate;
                    return new TeacherAttentionItemViewModel
                    {
                        AssignmentId = p.AssignmentId,
                        Title = info?.Title ?? "Assignment Submission",
                        CourseName = info?.CourseName ?? "Assigned Course",
                        PendingCount = p.PendingCount,
                        IsUrgent = isPastDue || p.PendingCount > 5,
                        DueDateFormatted = info != null ? (isPastDue ? $"Due {info.DueDate:MMM dd}" : $"Due {info.DueDate:MMM dd}") : "Pending",
                        ReviewUrl = $"/Teacher/Submissions?assignmentId={p.AssignmentId}"
                    };
                }).ToList();
            }

            // Assigned Courses List (Top 6 for dashboard)
            var teacherCourses = await _context.Courses
                .AsNoTracking()
                .Where(c => teacherCourseIds.Contains(c.Id))
                .Include(c => c.Major)
                .Include(c => c.Semester)
                .Include(c => c.Enrollments)
                .Include(c => c.Chapters)
                .Take(6)
                .ToListAsync();

            viewModel.Courses = teacherCourses.Select((c, index) => new TeacherCourseItemViewModel
            {
                Id = c.Id,
                Code = c.Code,
                Title = c.Title,
                MajorName = c.Major?.Name ?? "General",
                SemesterName = c.Semester?.Name ?? "Current Semester",
                StatusDisplay = c.Status.ToString(),
                CoverImageUrl = c.Thumbnail ?? "",
                StudentCount = c.Enrollments.Count,
                ModuleCount = c.Chapters.Count,
                ManageUrl = $"/Teacher/Courses/Details/{c.Id}",
                GradientClass = $"course-gradient-{(index % 3) + 1}",
                IconClass = index % 3 == 0 ? "bi-code-slash" : index % 3 == 1 ? "bi-cpu" : "bi-database",
                RoleLabel = c.TeacherId == teacherId ? "Instructor" : "Teaching Assistant"
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching teacher dashboard data for {TeacherId}", teacherId);
        }

        return viewModel;
    }

    public async Task<IEnumerable<TeacherCourseItemViewModel>> GetTeacherCoursesAsync(string teacherId)
    {
        var authorizedCourseIds = await GetAuthorizedCourseIdsAsync(teacherId);
        if (!authorizedCourseIds.Any()) return Enumerable.Empty<TeacherCourseItemViewModel>();

        var courses = await _context.Courses
            .AsNoTracking()
            .Where(c => authorizedCourseIds.Contains(c.Id))
            .Include(c => c.Major)
            .Include(c => c.Semester)
            .Include(c => c.Enrollments)
            .Include(c => c.Chapters)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var assistantPermissions = await _context.CourseAssistants
            .AsNoTracking()
            .Where(ca => ca.AssistantId == teacherId && authorizedCourseIds.Contains(ca.CourseId))
            .ToDictionaryAsync(ca => ca.CourseId, ca => ca.CanGrade);

        return courses.Select((c, index) =>
        {
            bool isPrimary = c.TeacherId == teacherId;
            bool canGrade = isPrimary || (assistantPermissions.TryGetValue(c.Id, out var cg) && cg);

            return new TeacherCourseItemViewModel
            {
                Id = c.Id,
                Code = c.Code,
                Title = c.Title,
                MajorName = c.Major?.Name ?? "General",
                SemesterName = c.Semester?.Name ?? "Current Semester",
                StatusDisplay = c.Status.ToString(),
                CoverImageUrl = c.Thumbnail ?? "",
                StudentCount = c.Enrollments.Count,
                ModuleCount = c.Chapters.Count,
                ManageUrl = $"/Teacher/Courses/Details/{c.Id}",
                GradientClass = $"course-gradient-{(index % 3) + 1}",
                IconClass = index % 3 == 0 ? "bi-code-slash" : index % 3 == 1 ? "bi-cpu" : "bi-database",
                RoleLabel = isPrimary ? "Instructor" : "Teaching Assistant",
                CanGrade = canGrade
            };
        });
    }

    public async Task<TeacherCourseDetailsViewModel?> GetTeacherCourseDetailsAsync(int courseId, string teacherId)
    {
        if (!await HasAccessToCourseAsync(courseId, teacherId))
        {
            return null;
        }

        var course = await _context.Courses
            .AsNoTracking()
            .Where(c => c.Id == courseId)
            .Include(c => c.Major)
            .Include(c => c.Semester)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.CourseItems)
                    .ThenInclude(ci => ci.Lesson)
            .Include(c => c.Enrollments)
                .ThenInclude(e => e.Student)
            .FirstOrDefaultAsync();

        if (course == null) return null;

        var allItems = course.Chapters.SelectMany(ch => ch.CourseItems).ToList();
        var videoLessonIds = allItems
            .Where(i => i.Type == ItemType.Lesson && i.Lesson != null && !string.IsNullOrWhiteSpace(i.Lesson.VideoUrl))
            .Select(i => i.Id)
            .ToList();
        int totalVideoLessons = videoLessonIds.Count;

        var videoStatsByStudent = await _context.VideoLessonProgresses
            .AsNoTracking()
            .Where(v => videoLessonIds.Contains(v.CourseItemId))
            .GroupBy(v => v.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                CompletedCount = g.Count(v => v.IsCompleted),
                LastActivityAt = g.Max(v => (DateTime?)v.LastActivityAt)
            })
            .ToDictionaryAsync(x => x.StudentId, x => (x.CompletedCount, x.LastActivityAt));

        bool isPrimary = course.TeacherId == teacherId;
        bool canGrade = await CanGradeCourseAsync(courseId, teacherId);

        var viewModel = new TeacherCourseDetailsViewModel
        {
            Id = course.Id,
            Code = course.Code,
            Title = course.Title,
            Description = course.Description,
            MajorName = course.Major?.Name ?? "General",
            SemesterName = course.Semester?.Name ?? "Current",
            Status = course.Status.ToString(),
            StudentCount = course.Enrollments.Count,
            ChapterCount = course.Chapters.Count,
            AssignmentCount = allItems.Count(i => i.Type == ItemType.Assignment),
            QuizCount = allItems.Count(i => i.Type == ItemType.Quiz),
            TotalVideoLessonsCount = totalVideoLessons,
            RoleLabel = isPrimary ? "Instructor" : "Teaching Assistant",
            CanGrade = canGrade,
            Chapters = course.Chapters.OrderBy(ch => ch.Position).Select(ch => new TeacherChapterViewModel
            {
                Id = ch.Id,
                Title = ch.Title,
                Position = ch.Position,
                ItemCount = ch.CourseItems.Count,
                Items = ch.CourseItems.OrderBy(i => i.Position).Select(i => new TeacherCourseItemDetailViewModel
                {
                    Id = i.Id,
                    Title = i.Title,
                    Type = i.Type
                }).ToList()
            }).ToList(),
            EnrolledStudents = course.Enrollments.OrderByDescending(e => e.EnrolledAt).Select(e =>
            {
                var (completedVideos, lastAt) = videoStatsByStudent.GetValueOrDefault(e.StudentId, (0, null));
                return new TeacherStudentRosterViewModel
                {
                    StudentId = e.StudentId,
                    FullName = e.Student?.FullName ?? e.Student?.UserName ?? "Student",
                    Email = e.Student?.Email ?? "",
                    UniversityId = e.Student?.UniversityId,
                    Avatar = e.Student?.Avatar ?? "",
                    EnrolledAt = e.EnrolledAt,
                    CompletedVideoLessons = completedVideos,
                    TotalVideoLessons = totalVideoLessons,
                    LastActivityAt = lastAt
                };
            }).ToList()
        };

        return viewModel;
    }

    public async Task<List<StudentLessonVideoDetailViewModel>?> GetStudentCourseVideoProgressAsync(
        int courseId, string studentId, string requesterId, bool isAdmin = false)
    {
        if (courseId <= 0 || string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(requesterId))
        {
            return null;
        }

        if (!isAdmin && !await HasAccessToCourseAsync(courseId, requesterId))
        {
            return null;
        }

        bool isEnrolled = await _context.Enrollments
            .AsNoTracking()
            .AnyAsync(e => e.CourseId == courseId && e.StudentId == studentId);
        if (!isEnrolled) return null;

        var videoLessons = await _context.CourseItems
            .AsNoTracking()
            .Where(ci => ci.Chapter.CourseId == courseId &&
                         ci.Type == ItemType.Lesson &&
                         ci.Lesson != null &&
                         !string.IsNullOrWhiteSpace(ci.Lesson.VideoUrl))
            .OrderBy(ci => ci.Chapter.Position)
            .ThenBy(ci => ci.Position)
            .Select(ci => new { ci.Id, ci.Title })
            .ToListAsync();

        var itemIds = videoLessons.Select(v => v.Id).ToList();
        var progressMap = await _context.VideoLessonProgresses
            .AsNoTracking()
            .Where(v => v.StudentId == studentId && itemIds.Contains(v.CourseItemId))
            .ToDictionaryAsync(v => v.CourseItemId);

        return videoLessons.Select(vl =>
        {
            progressMap.TryGetValue(vl.Id, out var p);
            int pct = p != null && p.VideoDurationSeconds > 0
                ? Math.Min(100, (int)Math.Round((p.QualifyingWatchedSeconds / p.VideoDurationSeconds) * 100.0))
                : (p?.IsCompleted == true ? 100 : 0);
            return new StudentLessonVideoDetailViewModel
            {
                CourseItemId = vl.Id,
                LessonTitle = vl.Title,
                QualifyingWatchedSeconds = p?.QualifyingWatchedSeconds ?? 0,
                VideoDurationSeconds = p?.VideoDurationSeconds ?? 0,
                WatchedPercentage = p?.IsCompleted == true ? Math.Max(90, pct) : pct,
                IsCompleted = p?.IsCompleted ?? false,
                LastActivityAt = p?.LastActivityAt
            };
        }).ToList();
    }

    public async Task<IEnumerable<TeacherAssignmentItemViewModel>> GetTeacherAssignmentsAsync(string teacherId, int? courseId = null)
    {
        var authorizedCourseIds = await GetAuthorizedCourseIdsAsync(teacherId);
        if (!authorizedCourseIds.Any()) return Enumerable.Empty<TeacherAssignmentItemViewModel>();

        if (courseId.HasValue && courseId.Value > 0)
        {
            if (!authorizedCourseIds.Contains(courseId.Value))
            {
                return Enumerable.Empty<TeacherAssignmentItemViewModel>();
            }
            authorizedCourseIds = new List<int> { courseId.Value };
        }

        var assignments = await _context.Assignments
            .AsNoTracking()
            .Where(a => authorizedCourseIds.Contains(a.CourseItem.Chapter.CourseId))
            .OrderByDescending(a => a.DueDate)
            .Select(a => new TeacherAssignmentItemViewModel
            {
                Id = a.Id,
                CourseId = a.CourseItem.Chapter.CourseId,
                CourseCode = a.CourseItem.Chapter.Course.Code,
                CourseTitle = a.CourseItem.Chapter.Course.Title,
                Title = a.CourseItem.Title,
                Instruction = a.Instruction,
                DueDate = a.DueDate,
                MaxScore = a.MaxScore,
                TotalSubmissions = a.Submissions.Count(),
                PendingSubmissions = a.Submissions.Count(s =>
                    (s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.Late) &&
                    !_context.AssignmentSubmissions.Any(newer =>
                        newer.AssignmentId == s.AssignmentId &&
                        newer.StudentId == s.StudentId &&
                        (newer.SubmittedAt > s.SubmittedAt ||
                         (newer.SubmittedAt == s.SubmittedAt && newer.Id > s.Id)))),
                GradedSubmissions = a.Submissions.Count(s => s.Status == SubmissionStatus.Graded)
            })
            .ToListAsync();

        return assignments;
    }

    public async Task<IEnumerable<TeacherSubmissionItemViewModel>> GetAssignmentSubmissionsAsync(int assignmentId, string teacherId)
    {
        var assignment = await _context.Assignments
            .AsNoTracking()
            .Where(a => a.Id == assignmentId)
            .Include(a => a.CourseItem)
                .ThenInclude(ci => ci.Chapter)
                    .ThenInclude(ch => ch.Course)
            .FirstOrDefaultAsync();

        if (assignment == null) return Enumerable.Empty<TeacherSubmissionItemViewModel>();

        var courseId = assignment.CourseItem?.Chapter?.CourseId ?? 0;
        if (!await HasAccessToCourseAsync(courseId, teacherId))
        {
            _logger.LogWarning("User {TeacherId} attempted unauthorized access to submissions for assignment {AssignmentId} in course {CourseId}",
                teacherId, assignmentId, courseId);
            return Enumerable.Empty<TeacherSubmissionItemViewModel>();
        }

        var canGrade = await CanGradeCourseAsync(courseId, teacherId);

        var submissions = await _context.AssignmentSubmissions
            .AsNoTracking()
            .Where(s => s.AssignmentId == assignmentId)
            .Include(s => s.Student)
            .Include(s => s.GradedBy)
            .Include(s => s.Attachments.Where(a => !a.IsDeleted))
            .OrderByDescending(s => s.SubmittedAt)
            .ToListAsync();

        return submissions.Select(s => new TeacherSubmissionItemViewModel
        {
            SubmissionId = s.Id,
            AssignmentId = s.AssignmentId,
            AssignmentTitle = assignment.CourseItem?.Title ?? "Assignment",
            MaxScore = assignment.MaxScore,
            StudentId = s.StudentId,
            StudentName = s.Student?.FullName ?? s.Student?.UserName ?? "Student",
            StudentEmail = s.Student?.Email ?? "",
            StudentUniversityId = s.Student?.UniversityId,
            StudentAvatar = s.Student?.Avatar ?? "",
            FileUrl = s.FileUrl,
            OriginalFileName = s.OriginalFileName,
            FileSizeBytes = s.FileSizeBytes,
            ExternalUrl = s.ExternalUrl,
            Attachments = s.Attachments.Where(a => !a.IsDeleted).Select(a => new TeacherAttachmentItemViewModel
            {
                Id = a.Id,
                OriginalFileName = a.OriginalFileName,
                FileSizeBytes = a.FileSizeBytes,
                ContentType = a.ContentType,
                IsDeleted = a.IsDeleted
            }).ToList(),
            AttemptNumber = s.AttemptNumber,
            IsLatest = s.IsLatest,
            TextResponse = s.TextResponse,
            Status = s.Status,
            Score = s.Score,
            Feedback = s.Feedback,
            SubmittedAt = s.SubmittedAt,
            GradedAt = s.GradedAt,
            GradedByName = s.GradedBy?.FullName ?? s.GradedBy?.Email,
            CourseId = courseId,
            CanGrade = canGrade
        });
    }

    public async Task<TeacherGradingResult> GradeSubmissionAsync(int submissionId, double score, string? feedback, string teacherId)
    {
        var submission = await _context.AssignmentSubmissions
            .Include(s => s.Assignment)
                .ThenInclude(a => a.CourseItem)
                    .ThenInclude(ci => ci.Chapter)
                        .ThenInclude(ch => ch.Course)
            .FirstOrDefaultAsync(s => s.Id == submissionId);

        if (submission == null)
        {
            return new TeacherGradingResult { Success = false, Message = "Submission not found." };
        }

        if (!submission.IsLatest)
        {
            return new TeacherGradingResult
            {
                Success = false,
                Message = "Only the latest assignment submission attempt can be graded. Historical attempts are read-only."
            };
        }

        var course = submission.Assignment?.CourseItem?.Chapter?.Course;
        if (course == null)
        {
            return new TeacherGradingResult { Success = false, Message = "Course assignment not found." };
        }

        var isPrimaryTeacher = course.TeacherId == teacherId;
        var assistantRecord = await _context.CourseAssistants
            .AsNoTracking()
            .FirstOrDefaultAsync(ca => ca.CourseId == course.Id && ca.AssistantId == teacherId);

        if (!isPrimaryTeacher)
        {
            if (assistantRecord == null)
            {
                _logger.LogWarning("User {TeacherId} attempted unauthorized grading on submission {SubmissionId} for unassigned course {CourseId}",
                    teacherId, submissionId, course.Id);
                return new TeacherGradingResult { Success = false, Message = "Unauthorized: You do not own this course assignment." };
            }

            if (!assistantRecord.CanGrade)
            {
                _logger.LogWarning("Assistance {TeacherId} attempted grading on submission {SubmissionId} without CanGrade permission",
                    teacherId, submissionId);
                return new TeacherGradingResult { Success = false, Message = "Unauthorized: You do not have grading authorization for this course." };
            }
        }

        if (score < 0 || score > submission.Assignment!.MaxScore)
        {
            return new TeacherGradingResult { Success = false, Message = $"Score must be between 0 and {submission.Assignment!.MaxScore}." };
        }

        submission.Score = score;
        submission.Feedback = feedback;
        submission.Status = SubmissionStatus.Graded;
        submission.GradedAt = DateTime.UtcNow;
        submission.GradedById = teacherId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Submission {SubmissionId} graded by user {TeacherId} with score {Score}", submissionId, teacherId, score);

        return new TeacherGradingResult
        {
            Success = true,
            Message = "Submission graded successfully!",
            SubmissionId = submissionId
        };
    }

    public async Task<IEnumerable<TeacherGradeItemViewModel>> GetTeacherGradesAsync(string teacherId, int? courseId = null)
    {
        var authorizedCourseIds = await GetAuthorizedCourseIdsAsync(teacherId);
        if (!authorizedCourseIds.Any()) return Enumerable.Empty<TeacherGradeItemViewModel>();

        if (courseId.HasValue && courseId.Value > 0)
        {
            if (!authorizedCourseIds.Contains(courseId.Value)) return Enumerable.Empty<TeacherGradeItemViewModel>();
            authorizedCourseIds = new List<int> { courseId.Value };
        }

        // 1. Latest Graded Assignment Submission per (AssignmentId, StudentId)
        // Deterministic ordering: (GradedAt ?? SubmittedAt) DESC, SubmittedAt DESC, Id DESC
        var assignmentGrades = await _context.AssignmentSubmissions
            .AsNoTracking()
            .Where(s => authorizedCourseIds.Contains(s.Assignment.CourseItem.Chapter.CourseId) &&
                        s.Status == SubmissionStatus.Graded &&
                        s.Score.HasValue &&
                        !_context.AssignmentSubmissions.Any(newer =>
                            newer.AssignmentId == s.AssignmentId &&
                            newer.StudentId == s.StudentId &&
                            newer.Status == SubmissionStatus.Graded &&
                            newer.Score.HasValue &&
                            ((newer.GradedAt ?? newer.SubmittedAt) > (s.GradedAt ?? s.SubmittedAt) ||
                             ((newer.GradedAt ?? newer.SubmittedAt) == (s.GradedAt ?? s.SubmittedAt) &&
                              (newer.SubmittedAt > s.SubmittedAt ||
                               (newer.SubmittedAt == s.SubmittedAt && newer.Id > s.Id))))))
            .Select(s => new TeacherGradeItemViewModel
            {
                StudentId = s.StudentId,
                StudentName = s.Student.FullName ?? s.Student.UserName ?? "Student",
                StudentEmail = s.Student.Email ?? "",
                StudentUniversityId = s.Student.UniversityId,
                CourseTitle = s.Assignment.CourseItem.Chapter.Course.Title,
                AssessmentTitle = s.Assignment.CourseItem.Title,
                AssessmentType = "Assignment",
                Score = s.Score!.Value,
                MaxScore = s.Assignment.MaxScore > 0 ? s.Assignment.MaxScore : 100.0,
                IsPassed = s.Score!.Value >= ((s.Assignment.MaxScore > 0 ? s.Assignment.MaxScore : 100.0) * 0.5),
                Date = s.GradedAt ?? s.SubmittedAt
            })
            .ToListAsync();

        // 2. Highest-Scoring Quiz Submission per (QuizId, StudentId)
        // Deterministic tie-breaker: Score DESC, SubmittedAt DESC, Id DESC
        var quizGrades = await _context.QuizSubmissions
            .AsNoTracking()
            .Where(q => authorizedCourseIds.Contains(q.Quiz.CourseItem.Chapter.CourseId) &&
                        !_context.QuizSubmissions.Any(other =>
                            other.QuizId == q.QuizId &&
                            other.StudentId == q.StudentId &&
                            (other.Score > q.Score ||
                             (other.Score == q.Score &&
                              (other.SubmittedAt > q.SubmittedAt ||
                               (other.SubmittedAt == q.SubmittedAt && other.Id > q.Id))))))
            .Select(q => new TeacherGradeItemViewModel
            {
                StudentId = q.StudentId,
                StudentName = q.Student.FullName ?? q.Student.UserName ?? "Student",
                StudentEmail = q.Student.Email ?? "",
                StudentUniversityId = q.Student.UniversityId,
                CourseTitle = q.Quiz.CourseItem.Chapter.Course.Title,
                AssessmentTitle = q.Quiz.CourseItem.Title,
                AssessmentType = "Quiz",
                Score = q.Score,
                MaxScore = 100.0,
                IsPassed = q.IsPassed,
                Date = q.SubmittedAt
            })
            .ToListAsync();

        return assignmentGrades
            .Concat(quizGrades)
            .OrderByDescending(g => g.Date)
            .ThenBy(g => g.StudentName)
            .ThenBy(g => g.AssessmentTitle);
    }

    public async Task<IEnumerable<TeacherQuizItemViewModel>> GetTeacherQuizzesAsync(string teacherId, int? courseId = null)
    {
        var authorizedCourseIds = await GetAuthorizedCourseIdsAsync(teacherId);
        if (!authorizedCourseIds.Any()) return Enumerable.Empty<TeacherQuizItemViewModel>();

        if (courseId.HasValue && courseId.Value > 0)
        {
            if (!authorizedCourseIds.Contains(courseId.Value)) return Enumerable.Empty<TeacherQuizItemViewModel>();
            authorizedCourseIds = new List<int> { courseId.Value };
        }

        var query = _context.Quizzes
            .AsNoTracking()
            .Where(q => authorizedCourseIds.Contains(q.CourseItem.Chapter.CourseId));

        var quizzes = await query
            .Include(q => q.CourseItem)
                .ThenInclude(ci => ci.Chapter)
                    .ThenInclude(ch => ch.Course)
            .Include(q => q.Questions)
            .Include(q => q.Submissions)
            .ToListAsync();

        return quizzes.Select(q => new TeacherQuizItemViewModel
        {
            QuizId = q.Id,
            CourseId = q.CourseItem.Chapter.CourseId,
            CourseCode = q.CourseItem.Chapter.Course.Code,
            CourseTitle = q.CourseItem.Chapter.Course.Title,
            QuizTitle = q.CourseItem.Title,
            TimeLimitMinutes = q.TimeLimitMinutes,
            PassingScore = q.PassingScore,
            IsPublished = q.IsPublished,
            QuestionCount = q.Questions.Count,
            TotalAttempts = q.Submissions.Count,
            AverageScore = q.Submissions.Any() ? Math.Round(q.Submissions.Average(s => s.Score), 1) : 0.0
        });
    }

    public async Task<IEnumerable<TeacherBookItemViewModel>> GetTeacherBooksAsync(string teacherId, int? courseId = null)
    {
        var authorizedCourseIds = await GetAuthorizedCourseIdsAsync(teacherId);
        if (!authorizedCourseIds.Any()) return Enumerable.Empty<TeacherBookItemViewModel>();

        if (courseId.HasValue && courseId.Value > 0)
        {
            if (!authorizedCourseIds.Contains(courseId.Value)) return Enumerable.Empty<TeacherBookItemViewModel>();
            authorizedCourseIds = new List<int> { courseId.Value };
        }

        var query = _context.Books
            .AsNoTracking()
            .Where(b => authorizedCourseIds.Contains(b.CourseItem.Chapter.CourseId));

        var books = await query
            .Include(b => b.CourseItem)
                .ThenInclude(ci => ci.Chapter)
                    .ThenInclude(ch => ch.Course)
            .ToListAsync();

        return books.Select(b => new TeacherBookItemViewModel
        {
            BookId = b.Id,
            CourseId = b.CourseItem.Chapter.CourseId,
            CourseTitle = b.CourseItem.Chapter.Course.Title,
            ItemTitle = b.CourseItem.Title,
            FileUrl = _htmlSanitizer.SanitizeResourceUrl(b.FileUrl) ?? string.Empty,
            FileType = b.FileType,
            FileSize = b.FileSize
        });
    }
}
