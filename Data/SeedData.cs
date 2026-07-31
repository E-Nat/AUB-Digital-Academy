using Microsoft.AspNetCore.Identity;
using AUBDigitalAcademy.Models;

namespace AUBDigitalAcademy.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Create roles
        string[] roles = { "Admin", "Teacher", "Student" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Create default Admin user
        var adminEmail = "admin@aub.edu.kh";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
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

        // Create a sample Teacher
        var teacherEmail = "teacher@aub.edu.kh";
        var teacherUser = await userManager.FindByEmailAsync(teacherEmail);
        if (teacherUser == null)
        {
            teacherUser = new ApplicationUser
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

        // Create a sample Student
        var studentEmail = "student@aub.edu.kh";
        var studentUser = await userManager.FindByEmailAsync(studentEmail);
        if (studentUser == null)
        {
            studentUser = new ApplicationUser
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
    }
}
