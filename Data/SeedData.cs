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
                hasRoles = await context.Roles.AnyAsync();
                hasUsers = await context.Users.AnyAsync();
            }
        }
        catch (Exception ex)
        {
            // A transient connection failure (e.g. LocalDB still starting) must never destroy data.
            logger.LogWarning(ex, "Database connectivity check failed during seed. Skipping seed to protect existing data.");
            return;
        }

        // isReady = database is reachable AND both roles and users are already seeded.
        bool isReady = canConnect && hasRoles && hasUsers;

        if (!isReady)
        {
            await context.Database.EnsureCreatedAsync();
        }

        // 1. Create roles
        string[] roles = { "Admin", "Teacher", "Student" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // 2. Create default Admin user
        var adminEmail = "admin@aub.edu.kh";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new User
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "System Administrator",
                UniversityId = "ADMIN001",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(adminUser, "Admin@123");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }

        // 3. Create a sample Teacher
        var teacherEmail = "teacher@aub.edu.kh";
        var teacherUser = await userManager.FindByEmailAsync(teacherEmail);
        if (teacherUser == null)
        {
            teacherUser = new User
            {
                UserName = teacherEmail,
                Email = teacherEmail,
                FullName = "John Smith",
                UniversityId = "TCH001",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(teacherUser, "Teacher@123");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(teacherUser, "Teacher");
            }
        }

        // 3b. Create second sample Teacher for multi-teacher assignment & edit testing
        var teacher2Email = "alanturing@aub.edu.kh";
        var teacher2User = await userManager.FindByEmailAsync(teacher2Email);
        if (teacher2User == null)
        {
            teacher2User = new User
            {
                UserName = teacher2Email,
                Email = teacher2Email,
                FullName = "Alan Turing",
                UniversityId = "TCH002",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(teacher2User, "Teacher@123");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(teacher2User, "Teacher");
            }
        }

        // 4. Create a sample Student
        var studentEmail = "student@aub.edu.kh";
        var studentUser = await userManager.FindByEmailAsync(studentEmail);
        if (studentUser == null)
        {
            studentUser = new User
            {
                UserName = studentEmail,
                Email = studentEmail,
                FullName = "Jane Doe",
                UniversityId = "STU001",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(studentUser, "Student@123");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(studentUser, "Student");
            }
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
            var teacher = await userManager.FindByEmailAsync("teacher@aub.edu.kh");
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
                }
            }
        }
    }
}
