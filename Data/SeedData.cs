using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Data;

public static class SeedData
{
    public static async Task InitializeAsync(
        IServiceProvider serviceProvider,
        bool? allowDevTaGrading = null,
        bool skipOrphanLessonRepair = false,
        bool ensureBaselineForTestDb = false,
        CancellationToken cancellationToken = default)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
        var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        if (ensureBaselineForTestDb)
        {
            await EnsureBaselineSchemaAsync(context);
            await context.Database.MigrateAsync(cancellationToken);
            await InitializeDeterministicTestBaselineAsync(
                roleManager,
                userManager,
                context,
                skipOrphanLessonRepair,
                cancellationToken);
            return;
        }

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

        // Determine whether this startup runs in local Development / integration test mode.
        var hostEnv = serviceProvider.GetService<IHostEnvironment>();
        bool isDevOrTest = allowDevTaGrading
            ?? (hostEnv?.IsDevelopment() == true ||
                Environment.GetCommandLineArgs().Any(a => a.StartsWith("--test-", StringComparison.OrdinalIgnoreCase)));

        // 1. Create all 4 required roles (idempotent; never modifies existing roles)
        string[] roles = { "Admin", "Teacher", "Assistance", "Student" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // 2. Seed initial local-only Development accounts ONLY when the user store is initially empty
        //    and ONLY in Development / --test-* runs. Existing users, passwords, lockouts, active flags,
        //    profile fields, and roles are NEVER overwritten or reset on startup, and deleted users are
        //    never recreated once the database has been initialized.
        hasUsers = await context.Users.AnyAsync();
        User? teacherUser = null;
        User? assistanceUser = null;

        if (!hasUsers)
        {
            if (isDevOrTest)
            {
                // LOCAL DEVELOPMENT / TEST ONLY credentials — never seeded in Staging/Production
                // and never used to reset existing accounts.
                await CreateSeedUserIfMissingAsync(
                    userManager, context,
                    email: "admin@aub.com",
                    fullName: "System Administrator",
                    universityId: "ADMIN001",
                    localDevPassword: "admin123",
                    role: "Admin");

                teacherUser = await CreateSeedUserIfMissingAsync(
                    userManager, context,
                    email: "teacher@aub.com",
                    fullName: "John Smith",
                    universityId: "TCH001",
                    localDevPassword: "teacher123",
                    role: "Teacher");

                assistanceUser = await CreateSeedUserIfMissingAsync(
                    userManager, context,
                    email: "ta@aub.com",
                    fullName: "Teaching Assistant",
                    universityId: "TA001",
                    localDevPassword: "assistance123",
                    role: "Assistance");

                await CreateSeedUserIfMissingAsync(
                    userManager, context,
                    email: "student@aub.com",
                    fullName: "Jane Doe",
                    universityId: "STU001",
                    localDevPassword: "student123",
                    role: "Student");
            }
            else
            {
                logger.LogWarning(
                    "Database contains no users in a non-Development environment. Skipping local development seed user creation; provision administrative accounts explicitly.");
            }
        }
        else
        {
            teacherUser = await userManager.FindByEmailAsync("teacher@aub.com");
            assistanceUser = await userManager.FindByEmailAsync("ta@aub.com");
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
        if (!skipOrphanLessonRepair)
        {
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
        }

        // 8. Ensure Assistance User is assigned to CS301 with CanGrade = true ONLY on initial Development/Test setup
        if (isDevOrTest)
        {
            var cs301Course = await context.Courses.FirstOrDefaultAsync(c => c.Code == "CS301");
            if (cs301Course != null && assistanceUser != null && assistanceUser.IsActive)
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
            }
        }
    }

    /// <summary>
    /// Creates a local-only Development seed user strictly when no matching user already exists.
    /// Never overwrites passwords, lockouts, active flags, profile data, or roles on existing accounts.
    /// </summary>
    private static async Task<User> CreateSeedUserIfMissingAsync(
        UserManager<User> userManager,
        ApplicationDbContext context,
        string email,
        string fullName,
        string universityId,
        string localDevPassword,
        string role)
    {
        var existingUser = await userManager.FindByEmailAsync(email)
                           ?? await context.Users.FirstOrDefaultAsync(u => u.UniversityId == universityId);

        if (existingUser != null)
        {
            return existingUser;
        }

        var user = new User
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            UniversityId = universityId,
            EmailConfirmed = true,
            IsActive = true,
            IsTelegramVerified = true
        };

        var createResult = await userManager.CreateAsync(user, localDevPassword);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create local development seed user '{email}': {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign role '{role}' to local development seed user '{email}': {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
        }

        return user;
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

    private static async Task InitializeDeterministicTestBaselineAsync(
        RoleManager<IdentityRole> roleManager,
        UserManager<User> userManager,
        ApplicationDbContext context,
        bool skipOrphanLessonRepair,
        CancellationToken cancellationToken)
    {
        // 1. Identity Roles
        string[] roles = { "Admin", "Teacher", "Assistance", "Student" };
        foreach (var roleName in roles)
        {
            var existingRoles = await context.Roles
                .Where(r => r.Name == roleName)
                .ToListAsync(cancellationToken);

            if (existingRoles.Count > 1)
            {
                throw new InvalidOperationException($"Duplicate Identity role '{roleName}' detected.");
            }

            if (existingRoles.Count == 0)
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to create role '{roleName}': {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
                }
            }
        }

        // 2. Baseline Users (never overwrite existing passwords, stamps, roles, active flags, Telegram, or logins)
        var baselineUserSpecs = new (string Email, string FullName, string UniversityId, string Password, string Role)[]
        {
            ("admin@aub.com", "System Administrator", "ADMIN001", "admin123", "Admin"),
            ("teacher@aub.com", "John Smith", "TCH001", "teacher123", "Teacher"),
            ("ta@aub.com", "Teaching Assistant", "TA001", "assistance123", "Assistance"),
            ("student@aub.com", "Jane Doe", "STU001", "student123", "Student")
        };

        var baselineUsersByEmail = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in baselineUserSpecs)
        {
            var matches = await context.Users
                .Where(u => u.Email == spec.Email || u.UniversityId == spec.UniversityId || u.UserName == spec.Email)
                .ToListAsync(cancellationToken);

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Conflicting or duplicate baseline user records found for '{spec.Email}' / '{spec.UniversityId}'.");
            }

            if (matches.Count == 1)
            {
                var existing = matches[0];
                if (!string.Equals(existing.Email, spec.Email, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(existing.UserName, spec.Email, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(existing.UniversityId, spec.UniversityId, StringComparison.Ordinal) ||
                    !existing.IsActive)
                {
                    throw new InvalidOperationException(
                        $"Existing baseline user '{spec.Email}' conflicts with expected identity or active state.");
                }

                if (!await userManager.IsInRoleAsync(existing, spec.Role))
                {
                    throw new InvalidOperationException(
                        $"Existing baseline user '{spec.Email}' is missing expected role '{spec.Role}'.");
                }

                baselineUsersByEmail[spec.Email] = existing;
                continue;
            }

            var created = new User
            {
                UserName = spec.Email,
                Email = spec.Email,
                FullName = spec.FullName,
                UniversityId = spec.UniversityId,
                EmailConfirmed = true,
                IsActive = true,
                IsTelegramVerified = true
            };

            var createResult = await userManager.CreateAsync(created, spec.Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create baseline user '{spec.Email}': {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
            }

            var addRoleResult = await userManager.AddToRoleAsync(created, spec.Role);
            if (!addRoleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to assign role '{spec.Role}' to baseline user '{spec.Email}': {string.Join("; ", addRoleResult.Errors.Select(e => e.Description))}");
            }

            baselineUsersByEmail[spec.Email] = created;
        }

        var teacherUser = baselineUsersByEmail["teacher@aub.com"];
        var assistanceUser = baselineUsersByEmail["ta@aub.com"];

        // 3. Categories
        string[] categoryNames = { "Computer Science & IT", "Business & Management" };
        var categoriesByName = new Dictionary<string, Category>(StringComparer.Ordinal);
        foreach (var categoryName in categoryNames)
        {
            var matches = await context.Categories
                .Where(c => c.Name == categoryName)
                .ToListAsync(cancellationToken);

            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"Duplicate category '{categoryName}' detected.");
            }

            if (matches.Count == 1)
            {
                categoriesByName[categoryName] = matches[0];
                continue;
            }

            var category = new Category { Name = categoryName };
            context.Categories.Add(category);
            await context.SaveChangesAsync(cancellationToken);
            categoriesByName[categoryName] = category;
        }

        // 4. Majors
        var majorSpecs = new (string Name, string Code, string CategoryName)[]
        {
            ("Software Engineering", "SE", "Computer Science & IT"),
            ("Computer Science", "CS", "Computer Science & IT"),
            ("Data Science & AI", "DSAI", "Computer Science & IT"),
            ("Business Administration", "BA", "Business & Management"),
            ("Finance & Banking", "FB", "Business & Management"),
            ("Digital Marketing", "DM", "Business & Management")
        };

        var majorsByCode = new Dictionary<string, Major>(StringComparer.Ordinal);
        foreach (var (name, code, categoryName) in majorSpecs)
        {
            var expectedCategoryId = categoriesByName[categoryName].Id;
            var matches = await context.Majors
                .Where(m => m.Code == code)
                .ToListAsync(cancellationToken);

            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"Duplicate major code '{code}' detected.");
            }

            if (matches.Count == 1)
            {
                if (matches[0].CategoryId != expectedCategoryId)
                {
                    throw new InvalidOperationException(
                        $"Existing major '{code}' has conflicting CategoryId {matches[0].CategoryId} (expected {expectedCategoryId}).");
                }

                majorsByCode[code] = matches[0];
                continue;
            }

            var major = new Major { Name = name, Code = code, CategoryId = expectedCategoryId };
            context.Majors.Add(major);
            await context.SaveChangesAsync(cancellationToken);
            majorsByCode[code] = major;
        }

        // 5. Semesters
        var semesterSpecs = new (string Name, string Code, DateTime StartDate, DateTime EndDate, bool IsCurrent)[]
        {
            ("Spring Semester 2026", "SP26", new DateTime(2026, 1, 15), new DateTime(2026, 5, 30), true),
            ("Fall Semester 2025", "FA25", new DateTime(2025, 9, 1), new DateTime(2025, 12, 20), false)
        };

        var semestersByCode = new Dictionary<string, Semester>(StringComparer.Ordinal);
        foreach (var (name, code, startDate, endDate, isCurrent) in semesterSpecs)
        {
            var matches = await context.Semesters
                .Where(s => s.Code == code)
                .ToListAsync(cancellationToken);

            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"Duplicate semester code '{code}' detected.");
            }

            if (matches.Count == 1)
            {
                var existing = matches[0];
                if (existing.StartDate != startDate || existing.EndDate != endDate || existing.IsCurrent != isCurrent)
                {
                    throw new InvalidOperationException(
                        $"Existing semester '{code}' has conflicting dates or IsCurrent flag.");
                }

                semestersByCode[code] = existing;
                continue;
            }

            var semester = new Semester
            {
                Name = name,
                Code = code,
                StartDate = startDate,
                EndDate = endDate,
                IsCurrent = isCurrent
            };
            context.Semesters.Add(semester);
            await context.SaveChangesAsync(cancellationToken);
            semestersByCode[code] = semester;
        }

        // 6–9. Foundational Courses, Chapters, CourseItems, and Lessons
        var sp26SemesterId = semestersByCode["SP26"].Id;
        var courseSpecs = new (string Code, string Title, string Description, string MajorCode, string Thumbnail, int CreatedDaysAgo, int UpdatedDaysAgo)[]
        {
            ("CS301", "Full-Stack Web Engineering", "Modern enterprise web architecture, secure RESTful APIs, relational databases, and cloud application deployment.", "SE", "/images/course_webdev.jpg", 30, 5),
            ("CS201", "Data Structures & Algorithms", "Fundamental data structures, computational complexity analysis, asymptotic efficiency, and algorithmic problem-solving in modern languages.", "CS", "/images/course_cybersecurity.jpg", 25, 4),
            ("AI401", "Applied Machine Learning & AI", "Foundational supervised and unsupervised learning models, neural networks, predictive data analytics, and real-world evaluation.", "DSAI", "/images/course_datascience.jpg", 20, 3),
            ("BA101", "Principles of Business Management", "Strategic organizational management, leadership frameworks, digital transformation, and business innovation in international markets.", "BA", "/images/course_uiux.jpg", 15, 2)
        };

        var itemSpecs = new (int Position, string Title)[]
        {
            (1, "Course Overview & Learning Objectives"),
            (2, "Core Architecture & System Setup"),
            (3, "Practical Implementation Workshop")
        };

        Course? cs301Course = null;
        foreach (var spec in courseSpecs)
        {
            var expectedMajorId = majorsByCode[spec.MajorCode].Id;
            var courseMatches = await context.Courses
                .Where(c => c.Code == spec.Code)
                .ToListAsync(cancellationToken);

            if (courseMatches.Count > 1)
            {
                throw new InvalidOperationException($"Duplicate course code '{spec.Code}' detected.");
            }

            Course course;
            if (courseMatches.Count == 1)
            {
                course = courseMatches[0];
                if (course.MajorId != expectedMajorId ||
                    course.SemesterId != sp26SemesterId ||
                    !string.Equals(course.TeacherId, teacherUser.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Existing course '{spec.Code}' has conflicting MajorId, SemesterId, or TeacherId.");
                }
            }
            else
            {
                course = new Course
                {
                    Code = spec.Code,
                    Title = spec.Title,
                    Description = spec.Description,
                    Status = AUBDigitalAcademy.Models.Enums.CourseStatus.Published,
                    MajorId = expectedMajorId,
                    SemesterId = sp26SemesterId,
                    TeacherId = teacherUser.Id,
                    Thumbnail = spec.Thumbnail,
                    CreatedAt = DateTime.UtcNow.AddDays(-spec.CreatedDaysAgo),
                    UpdatedAt = DateTime.UtcNow.AddDays(-spec.UpdatedDaysAgo)
                };
                context.Courses.Add(course);
                await context.SaveChangesAsync(cancellationToken);
            }

            if (spec.Code == "CS301")
            {
                cs301Course = course;
            }

            // Chapter at Position 1
            const string chapterTitle = "Module 1: Foundations & Architecture";
            var chapterMatches = await context.Chapters
                .Where(ch => ch.CourseId == course.Id && ch.Position == 1)
                .ToListAsync(cancellationToken);

            if (chapterMatches.Count > 1)
            {
                throw new InvalidOperationException($"Duplicate baseline chapter at Position 1 for course '{spec.Code}'.");
            }

            Chapter chapter;
            if (chapterMatches.Count == 1)
            {
                chapter = chapterMatches[0];
                if (!string.Equals(chapter.Title, chapterTitle, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Existing baseline chapter for course '{spec.Code}' has conflicting Title '{chapter.Title}'.");
                }
            }
            else
            {
                chapter = new Chapter
                {
                    Title = chapterTitle,
                    Position = 1,
                    CourseId = course.Id
                };
                context.Chapters.Add(chapter);
                await context.SaveChangesAsync(cancellationToken);
            }

            // CourseItems 1..3 and their 1:1 Lessons
            foreach (var (position, itemTitle) in itemSpecs)
            {
                var itemMatches = await context.CourseItems
                    .Where(ci => ci.ChapterId == chapter.Id && ci.Position == position)
                    .ToListAsync(cancellationToken);

                if (itemMatches.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"Duplicate CourseItem at Position {position} for course '{spec.Code}'.");
                }

                CourseItem item;
                if (itemMatches.Count == 1)
                {
                    item = itemMatches[0];
                    if (!string.Equals(item.Title, itemTitle, StringComparison.Ordinal) ||
                        item.Type != AUBDigitalAcademy.Models.Enums.ItemType.Lesson)
                    {
                        throw new InvalidOperationException(
                            $"Existing CourseItem at Position {position} for course '{spec.Code}' has conflicting Title or Type.");
                    }
                }
                else
                {
                    item = new CourseItem
                    {
                        ChapterId = chapter.Id,
                        Title = itemTitle,
                        Position = position,
                        Type = AUBDigitalAcademy.Models.Enums.ItemType.Lesson
                    };
                    context.CourseItems.Add(item);
                    await context.SaveChangesAsync(cancellationToken);
                }

                var lessonMatches = await context.Lessons
                    .Where(l => l.CourseItemId == item.Id)
                    .ToListAsync(cancellationToken);

                if (lessonMatches.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"Duplicate Lesson rows found for CourseItem {item.Id} in course '{spec.Code}'.");
                }

                if (lessonMatches.Count == 0)
                {
                    context.Lessons.Add(new Lesson
                    {
                        CourseItemId = item.Id,
                        ContentHtml = $"<p>Welcome to {item.Title}. Foundational academic concepts and practical frameworks.</p>",
                        VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
                    });
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
        }

        // Orphan Lesson repair is intentionally skipped in Enrollment Test Mode
        if (!skipOrphanLessonRepair)
        {
            var orphanLessonItems = await context.CourseItems
                .Where(ci => ci.Type == AUBDigitalAcademy.Models.Enums.ItemType.Lesson && !context.Lessons.Any(l => l.CourseItemId == ci.Id))
                .ToListAsync(cancellationToken);

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
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        // 10. CourseAssistant
        if (cs301Course == null)
        {
            throw new InvalidOperationException("Baseline course 'CS301' is missing.");
        }

        var assistantMatches = await context.CourseAssistants
            .Where(ca => ca.CourseId == cs301Course.Id && ca.AssistantId == assistanceUser.Id)
            .ToListAsync(cancellationToken);

        if (assistantMatches.Count > 1)
        {
            throw new InvalidOperationException("Duplicate CourseAssistant relationship detected for CS301 and ta@aub.com.");
        }

        if (assistantMatches.Count == 1)
        {
            if (!assistantMatches[0].CanGrade)
            {
                throw new InvalidOperationException(
                    "Existing CourseAssistant relationship for CS301 and ta@aub.com has CanGrade = false.");
            }
        }
        else
        {
            context.CourseAssistants.Add(new CourseAssistant
            {
                CourseId = cs301Course.Id,
                AssistantId = assistanceUser.Id,
                CanGrade = true,
                AssignedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
