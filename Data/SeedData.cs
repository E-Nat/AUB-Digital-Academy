using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
        var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Check connectivity and schema health safely — never drop the database on a transient error.
        bool canConnect = false;
        bool hasRoles = false;
        bool hasUsers = false;
        try
        {
            canConnect = await context.Database.CanConnectAsync();
            if (canConnect)
            {
                // Safely baseline pre-existing schema and ensure TelegramLinkTokens table exists
                await EnsureBaselineSchemaAsync(context);

                // Execute standard EF Core migrations for SQL Server
                await context.Database.MigrateAsync();
                hasRoles = await context.Roles.AnyAsync();
                hasUsers = await context.Users.AnyAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database connectivity or migration check encountered an issue during seed. Will attempt continuing.");
        }

        // 1. Create all 4 required roles
        string[] roles = { "Admin", "Teacher", "Assistance", "Student" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Detect whether legacy @aub.edu.kh seed accounts exist so we only clean up legacy/extra users during migration
        string[] legacyNormalizedEmails =
        {
            "ADMIN@AUB.EDU.KH",
            "TEACHER@AUB.EDU.KH",
            "ALANTURING@AUB.EDU.KH",
            "STUDENT@AUB.EDU.KH"
        };
        bool hasLegacySeedUsers = await context.Users
            .AnyAsync(u => u.NormalizedEmail != null && legacyNormalizedEmails.Contains(u.NormalizedEmail));

        // 2. Ensure only the 3 baseline accounts (Admin, Teacher, Student)
        var adminUser = await EnsureSeedUserAsync(
            userManager, context,
            email: "admin@aub.com",
            legacyEmail: "admin@aub.edu.kh",
            fullName: "System Administrator",
            universityId: "ADMIN001",
            password: "admin123",
            role: "Admin");

        var teacherUser = await EnsureSeedUserAsync(
            userManager, context,
            email: "teacher@aub.com",
            legacyEmail: "teacher@aub.edu.kh",
            fullName: "John Smith",
            universityId: "TCH001",
            password: "teacher123",
            role: "Teacher");

        var assistanceUser = await EnsureSeedUserAsync(
            userManager, context,
            email: "ta@aub.com",
            legacyEmail: "ta@aub.edu.kh",
            fullName: "Teaching Assistant",
            universityId: "TA001",
            password: "assistance123",
            role: "Assistance");

        var studentUser = await EnsureSeedUserAsync(
            userManager, context,
            email: "student@aub.com",
            legacyEmail: "student@aub.edu.kh",
            fullName: "Jane Doe",
            universityId: "STU001",
            password: "student123",
            role: "Student");

        // Remove legacy extra users (e.g. alanturing@aub.edu.kh and prior extra accounts) on migration
        if (hasLegacySeedUsers)
        {
            var keptIds = new[] { adminUser.Id, teacherUser.Id, assistanceUser.Id, studentUser.Id };
            var usersToRemove = await context.Users
                .Where(u => !keptIds.Contains(u.Id))
                .ToListAsync();

            await RemoveUsersSafelyAsync(context, userManager, usersToRemove, teacherUser.Id);
        }

        // 5. Seed Category & Majors if empty
        if (!await context.Categories.AnyAsync())
        {
            var csCategory = new Category { Name = "Computer Science & IT" };
            context.Categories.Add(csCategory);
            await context.SaveChangesAsync();

            if (!await context.Majors.AnyAsync())
            {
                context.Majors.AddRange(
                    new Major { Name = "Software Engineering", Code = "SE", CategoryId = csCategory.Id },
                    new Major { Name = "Computer Science", Code = "CS", CategoryId = csCategory.Id },
                    new Major { Name = "Data Science & AI", Code = "DSAI", CategoryId = csCategory.Id }
                );
                await context.SaveChangesAsync();
            }
        }

        // 5b. Ensure Business & Management Category and Majors exist
        var busCategory = await context.Categories.FirstOrDefaultAsync(c => c.Name == "Business & Management");
        if (busCategory == null)
        {
            busCategory = new Category { Name = "Business & Management" };
            context.Categories.Add(busCategory);
            await context.SaveChangesAsync();

            var newMajors = new List<Major>
            {
                new Major { Name = "Business Administration", Code = "BA", CategoryId = busCategory.Id },
                new Major { Name = "Finance & Banking", Code = "FB", CategoryId = busCategory.Id },
                new Major { Name = "Digital Marketing", Code = "DM", CategoryId = busCategory.Id }
            };
            foreach (var m in newMajors)
            {
                if (!await context.Majors.AnyAsync(x => x.Code == m.Code))
                {
                    context.Majors.Add(m);
                }
            }
            await context.SaveChangesAsync();
        }

        // 6. Seed Semesters if empty
        if (!await context.Semesters.AnyAsync())
        {
            context.Semesters.AddRange(
                new Semester
                {
                    Name = "Spring Semester 2026",
                    Code = "SP26",
                    StartDate = new DateTime(2026, 1, 15),
                    EndDate = new DateTime(2026, 5, 30),
                    IsCurrent = true
                },
                new Semester
                {
                    Name = "Fall Semester 2025",
                    Code = "FA25",
                    StartDate = new DateTime(2025, 9, 1),
                    EndDate = new DateTime(2025, 12, 20),
                    IsCurrent = false
                }
            );
            await context.SaveChangesAsync();
        }

        // 7. Seed Foundational Published Courses if empty
        if (!await context.Courses.AnyAsync())
        {
            var teacher = teacherUser;
            var semester = await context.Semesters.FirstOrDefaultAsync(s => s.Code == "SP26")
                           ?? await context.Semesters.FirstOrDefaultAsync();
            var seMajor = await context.Majors.FirstOrDefaultAsync(m => m.Code == "SE")
                          ?? await context.Majors.FirstOrDefaultAsync();
            var csMajor = await context.Majors.FirstOrDefaultAsync(m => m.Code == "CS")
                          ?? seMajor;
            var dsaiMajor = await context.Majors.FirstOrDefaultAsync(m => m.Code == "DSAI")
                            ?? seMajor;
            var baMajor = await context.Majors.FirstOrDefaultAsync(m => m.Code == "BA")
                          ?? seMajor;

            if (teacher != null && semester != null && seMajor != null)
            {
                var seedCourses = new List<Course>
                {
                    new Course
                    {
                        Code = "CS301",
                        Title = "Full-Stack Web Engineering",
                        Description = "Modern enterprise web architecture, secure RESTful APIs, relational databases, and cloud application deployment.",
                        Status = AUBDigitalAcademy.Models.Enums.CourseStatus.Published,
                        MajorId = seMajor.Id,
                        SemesterId = semester.Id,
                        TeacherId = teacher.Id,
                        Thumbnail = "/images/course_webdev.jpg",
                        CreatedAt = DateTime.UtcNow.AddDays(-30),
                        UpdatedAt = DateTime.UtcNow.AddDays(-5)
                    },
                    new Course
                    {
                        Code = "CS201",
                        Title = "Data Structures & Algorithms",
                        Description = "Fundamental data structures, computational complexity analysis, asymptotic efficiency, and algorithmic problem-solving in modern languages.",
                        Status = AUBDigitalAcademy.Models.Enums.CourseStatus.Published,
                        MajorId = csMajor!.Id,
                        SemesterId = semester.Id,
                        TeacherId = teacher.Id,
                        Thumbnail = "/images/course_cybersecurity.jpg",
                        CreatedAt = DateTime.UtcNow.AddDays(-25),
                        UpdatedAt = DateTime.UtcNow.AddDays(-4)
                    },
                    new Course
                    {
                        Code = "AI401",
                        Title = "Applied Machine Learning & AI",
                        Description = "Foundational supervised and unsupervised learning models, neural networks, predictive data analytics, and real-world evaluation.",
                        Status = AUBDigitalAcademy.Models.Enums.CourseStatus.Published,
                        MajorId = dsaiMajor!.Id,
                        SemesterId = semester.Id,
                        TeacherId = teacher.Id,
                        Thumbnail = "/images/course_datascience.jpg",
                        CreatedAt = DateTime.UtcNow.AddDays(-20),
                        UpdatedAt = DateTime.UtcNow.AddDays(-3)
                    },
                    new Course
                    {
                        Code = "BA101",
                        Title = "Principles of Business Management",
                        Description = "Strategic organizational management, leadership frameworks, digital transformation, and business innovation in international markets.",
                        Status = AUBDigitalAcademy.Models.Enums.CourseStatus.Published,
                        MajorId = baMajor!.Id,
                        SemesterId = semester.Id,
                        TeacherId = teacher.Id,
                        Thumbnail = "/images/course_uiux.jpg",
                        CreatedAt = DateTime.UtcNow.AddDays(-15),
                        UpdatedAt = DateTime.UtcNow.AddDays(-2)
                    }
                };

                context.Courses.AddRange(seedCourses);
                await context.SaveChangesAsync();

                foreach (var course in seedCourses)
                {
                    var ch = new Chapter
                    {
                        Title = "Module 1: Foundations & Architecture",
                        Position = 1,
                        CourseId = course.Id
                    };
                    context.Chapters.Add(ch);
                    await context.SaveChangesAsync();

                    var items = new List<CourseItem>
                    {
                        new CourseItem { ChapterId = ch.Id, Title = "Course Overview & Learning Objectives", Position = 1, Type = AUBDigitalAcademy.Models.Enums.ItemType.Lesson },
                        new CourseItem { ChapterId = ch.Id, Title = "Core Architecture & System Setup", Position = 2, Type = AUBDigitalAcademy.Models.Enums.ItemType.Lesson },
                        new CourseItem { ChapterId = ch.Id, Title = "Practical Implementation Workshop", Position = 3, Type = AUBDigitalAcademy.Models.Enums.ItemType.Lesson }
                    };
                    context.CourseItems.AddRange(items);
                    await context.SaveChangesAsync();

                    foreach (var ci in items)
                    {
                        context.Lessons.Add(new Lesson
                        {
                            CourseItemId = ci.Id,
                            ContentHtml = $"<p>Welcome to {ci.Title}. Foundational academic concepts and practical frameworks.</p>",
                            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
                        });
                    }
                    await context.SaveChangesAsync();
                }
            }
        }

        // 7b. Repair any orphaned CourseItems of Type Lesson that lack a Lesson row
        var orphanLessonItems = await context.CourseItems
            .Where(ci => ci.Type == AUBDigitalAcademy.Models.Enums.ItemType.Lesson && !context.Lessons.Any(l => l.CourseItemId == ci.Id))
            .ToListAsync();

        if (orphanLessonItems.Any())
        {
            foreach (var ci in orphanLessonItems)
            {
                context.Lessons.Add(new Lesson
                {
                    CourseItemId = ci.Id,
                    ContentHtml = $"<p>Welcome to {ci.Title}. Foundational academic concepts and practical frameworks.</p>",
                    VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
                });
            }
            await context.SaveChangesAsync();
        }

        // 8. Ensure Assistance User is assigned to CS301 with CanGrade = true (Owner Decision 14.2)
        var cs301Course = await context.Courses.FirstOrDefaultAsync(c => c.Code == "CS301");
        if (cs301Course != null && assistanceUser != null)
        {
            var existingAssignment = await context.CourseAssistants
                .FirstOrDefaultAsync(ca => ca.CourseId == cs301Course.Id && ca.AssistantId == assistanceUser.Id);

            if (existingAssignment == null)
            {
                context.CourseAssistants.Add(new CourseAssistant
                {
                    CourseId = cs301Course.Id,
                    AssistantId = assistanceUser.Id,
                    CanGrade = true,
                    AssignedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }
            else if (!existingAssignment.CanGrade)
            {
                existingAssignment.CanGrade = true;
                await context.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// Creates or migrates a baseline user account so its email, credentials, and role match configuration.
    /// </summary>
    private static async Task<User> EnsureSeedUserAsync(
        UserManager<User> userManager,
        ApplicationDbContext context,
        string email,
        string legacyEmail,
        string fullName,
        string universityId,
        string password,
        string role)
    {
        var user = await userManager.FindByEmailAsync(email)
                   ?? await userManager.FindByEmailAsync(legacyEmail)
                   ?? await context.Users.FirstOrDefaultAsync(u => u.UniversityId == universityId);

        if (user == null)
        {
            user = new User
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                UniversityId = universityId,
                EmailConfirmed = true,
                IsActive = true,
                IsTelegramVerified = true
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create seeded user '{email}': {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
            }
        }
        else
        {
            var uidConflict = await context.Users
                .FirstOrDefaultAsync(u => u.UniversityId == universityId && u.Id != user.Id);
            if (uidConflict != null)
            {
                uidConflict.UniversityId = null;
                await userManager.UpdateAsync(uidConflict);
            }

            user.UserName = email;
            user.Email = email;
            user.FullName = fullName;
            user.UniversityId = universityId;
            user.EmailConfirmed = true;
            user.IsActive = true;
            user.IsTelegramVerified = true;
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            user.UpdatedAt = DateTime.UtcNow;

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to update seeded user '{email}': {string.Join("; ", updateResult.Errors.Select(e => e.Description))}");
            }

            if (!await userManager.CheckPasswordAsync(user, password))
            {
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await userManager.ResetPasswordAsync(user, resetToken, password);
                if (!resetResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to reset password for seeded user '{email}': {string.Join("; ", resetResult.Errors.Select(e => e.Description))}");
                }
            }
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var extraRoles = currentRoles.Where(r => r != role).ToArray();
        if (extraRoles.Length > 0)
        {
            await userManager.RemoveFromRolesAsync(user, extraRoles);
        }
        if (!currentRoles.Contains(role))
        {
            await userManager.AddToRoleAsync(user, role);
        }

        return user;
    }

    /// <summary>
    /// Removes users after reassigning or clearing restricted foreign-key references.
    /// </summary>
    private static async Task RemoveUsersSafelyAsync(
        ApplicationDbContext context,
        UserManager<User> userManager,
        List<User> usersToRemove,
        string fallbackTeacherId)
    {
        if (usersToRemove.Count == 0)
        {
            return;
        }

        var removeIds = usersToRemove.Select(u => u.Id).ToList();

        var coursesToReassign = await context.Courses
            .Where(c => removeIds.Contains(c.TeacherId))
            .ToListAsync();
        foreach (var course in coursesToReassign)
        {
            course.TeacherId = fallbackTeacherId;
        }

        var quizSubs = await context.QuizSubmissions
            .Where(qs => removeIds.Contains(qs.StudentId))
            .ToListAsync();
        if (quizSubs.Count > 0)
        {
            context.QuizSubmissions.RemoveRange(quizSubs);
        }

        var assignmentSubs = await context.AssignmentSubmissions
            .Where(a => removeIds.Contains(a.StudentId))
            .ToListAsync();
        if (assignmentSubs.Count > 0)
        {
            context.AssignmentSubmissions.RemoveRange(assignmentSubs);
        }

        var gradedSubs = await context.AssignmentSubmissions
            .Where(a => a.GradedById != null && removeIds.Contains(a.GradedById))
            .ToListAsync();
        foreach (var sub in gradedSubs)
        {
            sub.GradedById = null;
        }

        var progresses = await context.CourseProgresses
            .Where(cp => removeIds.Contains(cp.StudentId))
            .ToListAsync();
        if (progresses.Count > 0)
        {
            context.CourseProgresses.RemoveRange(progresses);
        }

        var enrollments = await context.Enrollments
            .Where(e => removeIds.Contains(e.StudentId))
            .ToListAsync();
        if (enrollments.Count > 0)
        {
            context.Enrollments.RemoveRange(enrollments);
        }

        await context.SaveChangesAsync();

        foreach (var user in usersToRemove)
        {
            await userManager.DeleteAsync(user);
        }
    }

    private static async Task EnsureBaselineSchemaAsync(ApplicationDbContext context)
    {
        const string checkSql = @"
            IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AspNetRoles')
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '__EFMigrationsHistory')
                BEGIN
                    CREATE TABLE [__EFMigrationsHistory] (
                        [MigrationId] nvarchar(150) NOT NULL,
                        [ProductVersion] nvarchar(32) NOT NULL,
                        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                    );
                END

                IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261124185257_InitialSqlServerCreate')
                BEGIN
                    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                    VALUES (N'20261124185257_InitialSqlServerCreate', N'10.0.5');
                END

                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TelegramLinkTokens')
                BEGIN
                    CREATE TABLE [TelegramLinkTokens] (
                        [Id] int NOT NULL IDENTITY,
                        [UserId] nvarchar(450) NOT NULL,
                        [TokenHash] nvarchar(128) NOT NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [ExpiresAt] datetime2 NOT NULL,
                        [IsUsed] bit NOT NULL,
                        [UsedAt] datetime2 NULL,
                        [CreatedByIp] nvarchar(50) NULL,
                        CONSTRAINT [PK_TelegramLinkTokens] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_TelegramLinkTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
                    );

                    CREATE UNIQUE INDEX [IX_TelegramLinkTokens_TokenHash] ON [TelegramLinkTokens] ([TokenHash]);
                    CREATE INDEX [IX_TelegramLinkTokens_UserId_IsUsed_ExpiresAt] ON [TelegramLinkTokens] ([UserId], [IsUsed], [ExpiresAt]);
                END
            END";

        await context.Database.ExecuteSqlRawAsync(checkSql);
    }
}
