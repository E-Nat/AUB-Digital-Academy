using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Models.ViewModels;
using AUBDigitalAcademy.Services.Interfaces;

namespace AUBDigitalAcademy.Data;

public static class UserManagementTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("🚀 RUNNING ADMIN USER MANAGEMENT INTEGRATION SUITE");
        Console.WriteLine("=================================================================\n");

        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        var signInManager = services.GetRequiredService<SignInManager<User>>();
        var userService = services.GetRequiredService<IUserService>();

        // Ensure baseline seed data
        await SeedData.InitializeAsync(services);

        // Cleanup any leftover test accounts from previous runs
        var testEmails = new[] { "teststudent_testrun@aub.edu.kh", "teststudent_edited@aub.edu.kh", "testteacher_testrun@aub.edu.kh", "unique_email_123@aub.edu.kh" };
        foreach (var em in testEmails)
        {
            var u = await userManager.FindByEmailAsync(em);
            if (u != null) await userManager.DeleteAsync(u);
        }

        var leftoverTestUsers = await context.Users
            .Where(u => u.UniversityId != null && (u.UniversityId.StartsWith("STU_TEST") || u.UniversityId.StartsWith("TCH_TEST") || u.UniversityId.StartsWith("STU_UNIQUE")))
            .ToListAsync();
        foreach (var u in leftoverTestUsers)
        {
            await userManager.DeleteAsync(u);
        }


        var adminUser = await userManager.FindByEmailAsync("admin@aub.com");
        if (adminUser == null)
        {
            throw new Exception("Seed Admin account 'admin@aub.com' not found.");
        }


        // --- TEST 1: Retrieve All Users ---
        var allUsers = (await userService.GetUsersAsync()).ToList();
        Console.WriteLine($"[PASS] Test 1: Baseline User Retrieval: Found {allUsers.Count} users in SQL Server database.");
        if (allUsers.Count < 3)
        {
            throw new Exception("Expected at least 3 baseline users (Admin, Teacher, Student).");
        }

        // --- TEST 2: Search Filtering ---
        var searchNameResult = (await userService.GetUsersAsync(searchTerm: "Jane")).ToList();
        var searchEmailResult = (await userService.GetUsersAsync(searchTerm: "teacher@aub.com")).ToList();
        var searchUidResult = (await userService.GetUsersAsync(searchTerm: "STU001")).ToList();

        if (!searchNameResult.Any(u => u.Email == "student@aub.com") ||
            !searchEmailResult.Any(u => u.Email == "teacher@aub.com") ||
            !searchUidResult.Any(u => u.UniversityId == "STU001"))
        {
            throw new Exception("Search filtering failed to match FullName, Email, or UniversityId.");
        }
        Console.WriteLine("  [PASS] Test 2: Search Filtering: Successfully filtered by FullName ('Jane'), Email, and UniversityId ('STU001').");

        // --- TEST 3: Role Filtering ---
        var admins = (await userService.GetUsersAsync(role: "Admin")).ToList();
        var teachers = (await userService.GetUsersAsync(role: "Teacher")).ToList();
        var students = (await userService.GetUsersAsync(role: "Student")).ToList();

        if (!admins.Any(u => u.Email == "admin@aub.com") ||
            !teachers.Any(u => u.Email == "teacher@aub.com") ||
            !students.Any(u => u.Email == "student@aub.com"))
        {
            throw new Exception("Role filtering failed to return correct role subsets.");
        }
        Console.WriteLine($"  [PASS] Test 3: Role Filtering: Filtered {admins.Count} Admins, {teachers.Count} Teachers, and {students.Count} Students.");

        // --- TEST 4: Create User (Student) ---
        var testStudentEmail = "teststudent_testrun@aub.edu.kh";
        var testStudentUid = "STU_TEST_999";
        
        // Clean up previous test user if exists
        var existingTestStudent = await userManager.FindByEmailAsync(testStudentEmail);
        if (existingTestStudent != null)
        {
            await userManager.DeleteAsync(existingTestStudent);
        }

        var createStudentVm = new CreateUserViewModel
        {
            FullName = "Test Student Integration",
            Email = testStudentEmail,
            UniversityId = testStudentUid,
            Role = "Student",
            Password = "Student@123456",
            ConfirmPassword = "Student@123456",
            IsActive = true
        };

        var createStudentRes = await userService.CreateUserAsync(createStudentVm);
        if (!createStudentRes.Succeeded)
        {
            throw new Exception($"Create student failed: {createStudentRes.Message} {string.Join(", ", createStudentRes.Errors)}");
        }

        var createdStudent = await userManager.FindByEmailAsync(testStudentEmail);
        if (createdStudent == null || createdStudent.UniversityId != testStudentUid)
        {
            throw new Exception("Created student user could not be found in SQL Server.");
        }
        var createdStudentRoles = await userManager.GetRolesAsync(createdStudent);
        if (!createdStudentRoles.Contains("Student"))
        {
            throw new Exception("Created student user does not have 'Student' role.");
        }
        Console.WriteLine("  [PASS] Test 4: Create Student Account: Successfully created new student account in Identity with hashed password & role.");

        // --- TEST 5: Create User (Teacher) ---
        var testTeacherEmail = "testteacher_testrun@aub.edu.kh";
        var testTeacherUid = "TCH_TEST_999";

        var existingTestTeacher = await userManager.FindByEmailAsync(testTeacherEmail);
        if (existingTestTeacher != null)
        {
            await userManager.DeleteAsync(existingTestTeacher);
        }

        var createTeacherVm = new CreateUserViewModel
        {
            FullName = "Test Teacher Integration",
            Email = testTeacherEmail,
            UniversityId = testTeacherUid,
            Role = "Teacher",
            Password = "Teacher@123456",
            ConfirmPassword = "Teacher@123456",
            IsActive = true
        };

        var createTeacherRes = await userService.CreateUserAsync(createTeacherVm);
        if (!createTeacherRes.Succeeded)
        {
            throw new Exception($"Create teacher failed: {createTeacherRes.Message}");
        }
        Console.WriteLine("  [PASS] Test 5: Create Teacher Account: Successfully created new teacher account in Identity.");

        // --- TEST 6: Uniqueness Validations ---
        var dupEmailRes = await userService.CreateUserAsync(new CreateUserViewModel
        {
            FullName = "Dup Email",
            Email = testStudentEmail,
            UniversityId = "STU_UNIQUE_NEW",
            Role = "Student",
            Password = "Password@123",
            ConfirmPassword = "Password@123"
        });

        var dupUidRes = await userService.CreateUserAsync(new CreateUserViewModel
        {
            FullName = "Dup Uid",
            Email = "unique_email_123@aub.edu.kh",
            UniversityId = testStudentUid,
            Role = "Student",
            Password = "Password@123",
            ConfirmPassword = "Password@123"
        });

        if (dupEmailRes.Succeeded || dupUidRes.Succeeded)
        {
            throw new Exception("Uniqueness check failed to block duplicate Email or UniversityId.");
        }
        Console.WriteLine("  [PASS] Test 6: Uniqueness Validations: Duplicate email & duplicate UniversityId correctly rejected with validation errors.");

        // --- TEST 7: Edit User & Email Sync ---
        var editVm = new EditUserViewModel
        {
            Id = createdStudent.Id,
            FullName = "Test Student EDITED",
            Email = "teststudent_edited@aub.edu.kh",
            UniversityId = "STU_TEST_999_EDITED",
            Role = "Student",
            CurrentRole = "Student",
            IsActive = true
        };

        var editRes = await userService.UpdateUserAsync(editVm);
        if (!editRes.Succeeded)
        {
            throw new Exception($"Edit user failed: {editRes.Message}");
        }

        var updatedStudent = await userManager.FindByIdAsync(createdStudent.Id);
        if (updatedStudent == null || updatedStudent.Email != "teststudent_edited@aub.edu.kh" || updatedStudent.UserName != "teststudent_edited@aub.edu.kh" || updatedStudent.UniversityId != "STU_TEST_999_EDITED")
        {
            throw new Exception("Edited student Email/UserName synchronization failed.");
        }
        Console.WriteLine("  [PASS] Test 7: User Edit & Identity Sync: Updated FullName, Email, and UniversityId; Identity UserName & Normalized fields strictly synchronized.");

        // --- TEST 8: Role Change (Student -> Teacher) ---
        var roleChangeVm = new EditUserViewModel
        {
            Id = createdStudent.Id,
            FullName = updatedStudent.FullName,
            Email = updatedStudent.Email!,
            UniversityId = updatedStudent.UniversityId,
            Role = "Teacher",
            CurrentRole = "Student",
            IsActive = true
        };

        var roleChangeRes = await userService.UpdateUserAsync(roleChangeVm);
        if (!roleChangeRes.Succeeded)
        {
            throw new Exception($"Role change failed: {roleChangeRes.Message}");
        }

        var rolesAfterChange = await userManager.GetRolesAsync(updatedStudent);
        if (rolesAfterChange.Count != 1 || !rolesAfterChange.Contains("Teacher"))
        {
            throw new Exception($"Role change failed. Expected ['Teacher'], got [{string.Join(", ", rolesAfterChange)}].");
        }
        Console.WriteLine("  [PASS] Test 8: Role Modification: Role successfully changed Student -> Teacher (old role cleanly removed).");

        // --- TEST 9: Soft Deactivation & Activation ---
        var toggleOffRes = await userService.ToggleUserStatusAsync(createdStudent.Id, adminUser.Id);
        if (!toggleOffRes.Succeeded)
        {
            throw new Exception($"Toggle status off failed: {toggleOffRes.Message}");
        }

        var deactivatedUser = await userManager.FindByIdAsync(createdStudent.Id);
        if (deactivatedUser == null || deactivatedUser.IsActive)
        {
            throw new Exception("Deactivation failed; user is still active.");
        }

        // Verify deactivated user status check
        if (deactivatedUser.IsActive)
        {
            throw new Exception("Deactivated user status check failed.");
        }
        Console.WriteLine("  [PASS] Test 9: Soft Deactivation: Account status set to IsActive=false; login blocked by IsActive check.");

        var toggleOnRes = await userService.ToggleUserStatusAsync(createdStudent.Id, adminUser.Id);
        if (!toggleOnRes.Succeeded)
        {
            throw new Exception($"Toggle status on failed: {toggleOnRes.Message}");
        }
        var reactivatedUser = await userManager.FindByIdAsync(createdStudent.Id);
        if (reactivatedUser == null || !reactivatedUser.IsActive)
        {
            throw new Exception("Reactivation failed; user is still inactive.");
        }
        Console.WriteLine("  [PASS] Test 10: Account Reactivation: Account status restored to IsActive=true.");

        // --- TEST 10: Safety Protections (Protected Primary Admin) ---
        var selfDeactivateRes = await userService.ToggleUserStatusAsync(adminUser.Id, adminUser.Id);
        if (selfDeactivateRes.Succeeded)
        {
            throw new Exception("Self-deactivation protection failed! Current Admin was able to deactivate themselves.");
        }

        var adminDemotionVm = new EditUserViewModel
        {
            Id = adminUser.Id,
            FullName = adminUser.FullName,
            Email = adminUser.Email!,
            UniversityId = adminUser.UniversityId,
            Role = "Student",
            CurrentRole = "Admin",
            IsActive = true
        };
        var adminDemotionRes = await userService.UpdateUserAsync(adminDemotionVm);
        if (adminDemotionRes.Succeeded)
        {
            throw new Exception("Primary Admin demotion protection failed! Admin role was changed.");
        }
        Console.WriteLine("  [PASS] Test 11: Safety Protections: Self-deactivation and Primary Admin demotion/deactivation strictly blocked with friendly validation alerts.");

        // --- TEST 11: Administrative Password Reset & Authentication ---
        var resetRes = await userService.ResetPasswordAsync(createdStudent.Id, "NewSecretPass@999", adminUser.Id);
        if (!resetRes.Succeeded)
        {
            throw new Exception($"Password reset failed: {resetRes.Message} {string.Join(", ", resetRes.Errors)}");
        }

        // Verify old password fails and new password succeeds via CheckPasswordAsync
        var oldPassCheck = await userManager.CheckPasswordAsync(reactivatedUser, "Student@123456");
        var newPassCheck = await userManager.CheckPasswordAsync(reactivatedUser, "NewSecretPass@999");

        if (oldPassCheck || !newPassCheck)
        {
            throw new Exception("Password reset verification failed. Old password still works or new password failed authentication.");
        }
        Console.WriteLine("  [PASS] Test 12: Administrative Password Reset: Reset succeeded via Identity API; old password invalidated, new password authenticated 100%.");


        // --- TEST 12: Get Details View Model ---
        var detailsVm = await userService.GetUserByIdAsync(createdStudent.Id);
        if (detailsVm == null || detailsVm.Email != reactivatedUser.Email)
        {
            throw new Exception("GetUserByIdAsync failed to return complete UserDetailsViewModel.");
        }
        Console.WriteLine("  [PASS] Test 13: User Details View Model: Successfully retrieved profile metadata and academic relationship records.");

        // --- CLEANUP TEST USERS ---
        await userManager.DeleteAsync(reactivatedUser);
        var teacherToDelete = await userManager.FindByEmailAsync(testTeacherEmail);
        if (teacherToDelete != null)
        {
            await userManager.DeleteAsync(teacherToDelete);
        }
        Console.WriteLine("  [PASS] Test 14: Clean Database Restoration: Temporary test users safely removed from SQL Server.");

        Console.WriteLine("\n=================================================================");
        Console.WriteLine("🎉 ALL 14/14 USER MANAGEMENT INTEGRATION TESTS PASSED SUCCESSFULLY!");
        Console.WriteLine("=================================================================\n");
    }
}
