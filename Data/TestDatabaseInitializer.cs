using System.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AUBDigitalAcademy.Data;

public sealed record TestInitializationResult(
    bool IsSuccess,
    string DiagnosticMessage)
{
    public static TestInitializationResult Success(string message) => new(true, message);
    public static TestInitializationResult Fail(string message) => new(false, message);
}

public static class TestDatabaseInitializer
{
    public const string LockResource = "AUBDigitalAcademy_Test:SeedInit:v1";
    public const int LockTimeoutMs = 30000;
    public const int LockCommandTimeoutSeconds = 45;
    public const int ReleaseCommandTimeoutSeconds = 15;

    public static async Task<TestInitializationResult> InitializeAsync(
        string testConnectionString,
        IServiceProvider rootServices,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(testConnectionString))
        {
            return TestInitializationResult.Fail("Test database connection string is missing.");
        }

        // Isolate Pooling=false strictly to the initialization lock connection so DisposeAsync()
        // deterministically closes the physical SQL Server session if sp_releaseapplock fails.
        SqlConnectionStringBuilder lockBuilder;
        try
        {
            lockBuilder = new SqlConnectionStringBuilder(testConnectionString)
            {
                Pooling = false
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            return TestInitializationResult.Fail($"Malformed test database connection string ({ex.GetType().Name}).");
        }

        await using var lockConnection = new SqlConnection(lockBuilder.ConnectionString);
        bool lockAcquired = false;
        bool initSucceeded = false;
        bool cleanupSucceeded = true;
        var errors = new List<string>();

        try
        {
            await lockConnection.OpenAsync(cancellationToken);

            await using (var dbCheckCmd = lockConnection.CreateCommand())
            {
                dbCheckCmd.CommandText = "SELECT DB_NAME()";
                dbCheckCmd.CommandTimeout = ReleaseCommandTimeoutSeconds;
                var actualDb = (await dbCheckCmd.ExecuteScalarAsync(cancellationToken) as string)?.Trim() ?? string.Empty;
                if (!string.Equals(actualDb, TestDatabaseGuard.RequiredTestDatabaseName, StringComparison.OrdinalIgnoreCase))
                {
                    var reported = string.IsNullOrEmpty(actualDb) ? "<empty>" : actualDb;
                    return TestInitializationResult.Fail(
                        $"Initializer connected to unexpected database '{reported}' instead of '{TestDatabaseGuard.RequiredTestDatabaseName}'.");
                }
            }

            await using (var lockCmd = lockConnection.CreateCommand())
            {
                lockCmd.CommandText = @"
                    DECLARE @result int;
                    EXEC @result = sp_getapplock
                        @Resource = @resource,
                        @LockMode = 'Exclusive',
                        @LockOwner = 'Session',
                        @LockTimeout = @timeoutMs;
                    SELECT @result;";
                lockCmd.CommandTimeout = LockCommandTimeoutSeconds;
                lockCmd.Parameters.AddWithValue("@resource", LockResource);
                lockCmd.Parameters.AddWithValue("@timeoutMs", LockTimeoutMs);

                var rawLockResult = await lockCmd.ExecuteScalarAsync(cancellationToken);
                int lockCode = Convert.ToInt32(rawLockResult);
                if (lockCode < 0)
                {
                    return TestInitializationResult.Fail(
                        $"Failed to acquire SQL Server initialization lock '{LockResource}' (sp_getapplock returned {lockCode}).");
                }

                lockAcquired = true;
            }

            await using (var scope = rootServices.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Database.SetDbConnection(lockConnection, contextOwnsConnection: false);

                await SeedData.InitializeAsync(
                    scope.ServiceProvider,
                    allowDevTaGrading: true,
                    skipOrphanLessonRepair: true,
                    ensureBaselineForTestDb: true,
                    cancellationToken: cancellationToken);

                var readiness = await VerifyBaselineReadinessAsync(context, cancellationToken);
                if (!readiness.IsSuccess)
                {
                    errors.Add(readiness.DiagnosticMessage);
                }
                else
                {
                    initSucceeded = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            errors.Add("Test database initialization was cancelled.");
        }
        catch (Exception ex)
        {
            errors.Add($"Initialization error ({ex.GetType().Name}): {ex.Message}");
        }
        finally
        {
            if (lockAcquired)
            {
                if (lockConnection.State == ConnectionState.Open)
                {
                    try
                    {
                        await using var releaseCmd = lockConnection.CreateCommand();
                        releaseCmd.CommandText = @"
                            DECLARE @res int;
                            EXEC @res = sp_releaseapplock
                                @Resource = @resource,
                                @LockOwner = 'Session';
                            SELECT @res;";
                        releaseCmd.CommandTimeout = ReleaseCommandTimeoutSeconds;
                        releaseCmd.Parameters.AddWithValue("@resource", LockResource);

                        var rawRelease = await releaseCmd.ExecuteScalarAsync(CancellationToken.None);
                        int releaseCode = Convert.ToInt32(rawRelease);
                        if (releaseCode < 0)
                        {
                            cleanupSucceeded = false;
                            errors.Add($"sp_releaseapplock failed with negative return code {releaseCode}.");
                            try { SqlConnection.ClearPool(lockConnection); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        cleanupSucceeded = false;
                        errors.Add($"sp_releaseapplock exception ({ex.GetType().Name}): {ex.Message}");
                        try { SqlConnection.ClearPool(lockConnection); } catch { }
                    }
                }
                else
                {
                    cleanupSucceeded = false;
                    errors.Add($"Lock connection was not open for sp_releaseapplock (State: {lockConnection.State}).");
                }
            }

            try
            {
                await lockConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                cleanupSucceeded = false;
                errors.Add($"Lock connection disposal failed ({ex.GetType().Name}): {ex.Message}");
            }
        }

        if (initSucceeded && cleanupSucceeded && errors.Count == 0)
        {
            return TestInitializationResult.Success(
                $"Verified and locked initialization of '{TestDatabaseGuard.RequiredTestDatabaseName}'.");
        }

        return TestInitializationResult.Fail(string.Join(" | ", errors));
    }

    public static async Task<TestInitializationResult> VerifyBaselineReadinessAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. No pending EF Core migrations
        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pendingMigrations.Count > 0)
        {
            return TestInitializationResult.Fail(
                $"Pending EF Core migrations remain: {string.Join(", ", pendingMigrations)}.");
        }

        // 2. Exactly one instance of each required role
        string[] requiredRoles = { "Admin", "Teacher", "Assistance", "Student" };
        var roles = await context.Roles
            .AsNoTracking()
            .Where(r => r.Name != null && requiredRoles.Contains(r.Name))
            .ToListAsync(cancellationToken);

        var roleMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var roleName in requiredRoles)
        {
            var matches = roles.Where(r => string.Equals(r.Name, roleName, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1)
            {
                return TestInitializationResult.Fail(
                    $"Expected exactly 1 '{roleName}' role, found {matches.Count}.");
            }

            roleMap[roleName] = matches[0].Id;
        }

        // 3. All four baseline users exist with expected identity, active status, and role
        var baselineUsers = new (string Email, string UniversityId, string Role)[]
        {
            ("admin@aub.com", "ADMIN001", "Admin"),
            ("teacher@aub.com", "TCH001", "Teacher"),
            ("ta@aub.com", "TA001", "Assistance"),
            ("student@aub.com", "STU001", "Student")
        };

        var userIdsByEmail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (email, universityId, role) in baselineUsers)
        {
            var matches = await context.Users
                .AsNoTracking()
                .Where(u => u.Email == email || u.UniversityId == universityId || u.UserName == email)
                .ToListAsync(cancellationToken);

            if (matches.Count != 1)
            {
                return TestInitializationResult.Fail(
                    $"Expected exactly 1 baseline user for '{email}' ({universityId}), found {matches.Count}.");
            }

            var user = matches[0];
            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(user.UserName, email, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(user.UniversityId, universityId, StringComparison.Ordinal) ||
                !user.IsActive)
            {
                return TestInitializationResult.Fail(
                    $"Baseline user '{email}' failed identity or active-status verification.");
            }

            var expectedRoleId = roleMap[role];
            var hasRole = await context.UserRoles
                .AsNoTracking()
                .AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == expectedRoleId, cancellationToken);

            if (!hasRole)
            {
                return TestInitializationResult.Fail(
                    $"Baseline user '{email}' is missing required role '{role}'.");
            }

            userIdsByEmail[email] = user.Id;
        }

        // 4. Both baseline categories exist
        string[] requiredCategories = { "Computer Science & IT", "Business & Management" };
        var categoryIdsByName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var catName in requiredCategories)
        {
            var matches = await context.Categories
                .AsNoTracking()
                .Where(c => c.Name == catName)
                .ToListAsync(cancellationToken);

            if (matches.Count != 1)
            {
                return TestInitializationResult.Fail(
                    $"Expected exactly 1 category '{catName}', found {matches.Count}.");
            }

            categoryIdsByName[catName] = matches[0].Id;
        }

        // 5. All six baseline majors exist with expected CategoryId
        var expectedMajors = new (string Code, string CategoryName)[]
        {
            ("SE", "Computer Science & IT"),
            ("CS", "Computer Science & IT"),
            ("DSAI", "Computer Science & IT"),
            ("BA", "Business & Management"),
            ("FB", "Business & Management"),
            ("DM", "Business & Management")
        };

        var majorIdsByCode = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (code, categoryName) in expectedMajors)
        {
            var matches = await context.Majors
                .AsNoTracking()
                .Where(m => m.Code == code)
                .ToListAsync(cancellationToken);

            if (matches.Count != 1)
            {
                return TestInitializationResult.Fail(
                    $"Expected exactly 1 major '{code}', found {matches.Count}.");
            }

            var major = matches[0];
            if (major.CategoryId != categoryIdsByName[categoryName])
            {
                return TestInitializationResult.Fail(
                    $"Major '{code}' has conflicting CategoryId {major.CategoryId}.");
            }

            majorIdsByCode[code] = major.Id;
        }

        // 6. Both baseline semesters exist with expected dates and IsCurrent
        var expectedSemesters = new (string Code, DateTime StartDate, DateTime EndDate, bool IsCurrent)[]
        {
            ("SP26", new DateTime(2026, 1, 15), new DateTime(2026, 5, 30), true),
            ("FA25", new DateTime(2025, 9, 1), new DateTime(2025, 12, 20), false)
        };

        var semesterIdsByCode = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (code, startDate, endDate, isCurrent) in expectedSemesters)
        {
            var matches = await context.Semesters
                .AsNoTracking()
                .Where(s => s.Code == code)
                .ToListAsync(cancellationToken);

            if (matches.Count != 1)
            {
                return TestInitializationResult.Fail(
                    $"Expected exactly 1 semester '{code}', found {matches.Count}.");
            }

            var sem = matches[0];
            if (sem.StartDate != startDate || sem.EndDate != endDate || sem.IsCurrent != isCurrent)
            {
                return TestInitializationResult.Fail(
                    $"Semester '{code}' has conflicting dates or IsCurrent flag.");
            }

            semesterIdsByCode[code] = sem.Id;
        }

        // 7–10. Four foundational courses, chapters, CourseItems (1..3), and linked Lessons
        var teacherId = userIdsByEmail["teacher@aub.com"];
        var sp26Id = semesterIdsByCode["SP26"];
        var expectedCourses = new (string Code, string MajorCode)[]
        {
            ("CS301", "SE"),
            ("CS201", "CS"),
            ("AI401", "DSAI"),
            ("BA101", "BA")
        };
        var expectedItems = new (int Position, string Title)[]
        {
            (1, "Course Overview & Learning Objectives"),
            (2, "Core Architecture & System Setup"),
            (3, "Practical Implementation Workshop")
        };

        int cs301CourseId = 0;
        foreach (var (courseCode, majorCode) in expectedCourses)
        {
            var courseMatches = await context.Courses
                .AsNoTracking()
                .Where(c => c.Code == courseCode)
                .ToListAsync(cancellationToken);

            if (courseMatches.Count != 1)
            {
                return TestInitializationResult.Fail(
                    $"Expected exactly 1 course '{courseCode}', found {courseMatches.Count}.");
            }

            var course = courseMatches[0];
            if (course.MajorId != majorIdsByCode[majorCode] ||
                course.SemesterId != sp26Id ||
                !string.Equals(course.TeacherId, teacherId, StringComparison.Ordinal))
            {
                return TestInitializationResult.Fail(
                    $"Course '{courseCode}' has conflicting MajorId, SemesterId, or TeacherId.");
            }

            if (courseCode == "CS301")
            {
                cs301CourseId = course.Id;
            }

            var chapterMatches = await context.Chapters
                .AsNoTracking()
                .Where(ch => ch.CourseId == course.Id && ch.Position == 1)
                .ToListAsync(cancellationToken);

            if (chapterMatches.Count != 1 ||
                !string.Equals(chapterMatches[0].Title, "Module 1: Foundations & Architecture", StringComparison.Ordinal))
            {
                return TestInitializationResult.Fail(
                    $"Course '{courseCode}' baseline chapter at Position 1 is missing, duplicated, or has conflicting Title.");
            }

            var chapterId = chapterMatches[0].Id;
            foreach (var (position, title) in expectedItems)
            {
                var itemMatches = await context.CourseItems
                    .AsNoTracking()
                    .Where(ci => ci.ChapterId == chapterId && ci.Position == position)
                    .ToListAsync(cancellationToken);

                if (itemMatches.Count != 1 ||
                    !string.Equals(itemMatches[0].Title, title, StringComparison.Ordinal) ||
                    itemMatches[0].Type != ItemType.Lesson)
                {
                    return TestInitializationResult.Fail(
                        $"Course '{courseCode}' CourseItem at Position {position} is missing, duplicated, or conflicting.");
                }

                var lessonCount = await context.Lessons
                    .AsNoTracking()
                    .CountAsync(l => l.CourseItemId == itemMatches[0].Id, cancellationToken);

                if (lessonCount != 1)
                {
                    return TestInitializationResult.Fail(
                        $"Course '{courseCode}' CourseItem {itemMatches[0].Id} expected 1 Lesson, found {lessonCount}.");
                }
            }
        }

        // 11. CourseAssistant relationship for CS301 and ta@aub.com
        var taId = userIdsByEmail["ta@aub.com"];
        var assistantMatches = await context.CourseAssistants
            .AsNoTracking()
            .Where(ca => ca.CourseId == cs301CourseId && ca.AssistantId == taId)
            .ToListAsync(cancellationToken);

        if (assistantMatches.Count != 1 || !assistantMatches[0].CanGrade)
        {
            return TestInitializationResult.Fail(
                "Required CourseAssistant relationship for CS301 and ta@aub.com (CanGrade = true) is missing, duplicated, or conflicting.");
        }

        return TestInitializationResult.Success("All baseline readiness checks passed.");
    }
}
