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

        // Ensure SQL Server database and tables are created cleanly
        bool isReady = false;
        try
        {
            isReady = await context.Database.CanConnectAsync() && await context.Roles.AnyAsync();
        }
        catch
        {
            // Incomplete database schema from previous failed run - drop and recreate
            await context.Database.EnsureDeletedAsync();
        }

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
    }
}
