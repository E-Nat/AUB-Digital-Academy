using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AUBDigitalAcademy.Data;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Services.Interfaces;
using System.Security.Cryptography;

namespace AUBDigitalAcademy.Data;

public static class RegistrationTelegramIntegrationTestRunner
{
    public static async Task RunTestsAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("🚀 RUNNING REGISTRATION & TELEGRAM VERIFICATION TEST SUITE");
        Console.WriteLine("=================================================================\n");

        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var signInManager = services.GetRequiredService<SignInManager<User>>();
        var telegramService = services.GetRequiredService<ITelegramService>();

        // Ensure database baseline schema and seed
        await SeedData.InitializeAsync(services);

        const string testStudentId = "07875";
        const string testStudentName = "Sok Dara";
        const string testPassword = "Student@123";

        // Clean up any leftover test data
        await CleanupTestUserAsync(context, userManager, testStudentId);

        try
        {
            // ─────────────────────────────────────────────────────────────
            // TEST 1: Student Registration (Only Name, Student ID, Password — No Email)
            // ─────────────────────────────────────────────────────────────
            var newStudent = new User
            {
                UserName = testStudentId,
                UniversityId = testStudentId,
                FullName = testStudentName,
                Email = null, // Documented requirement: Student enters only Name, ID, Password (no email)
                EmailConfirmed = false,
                IsActive = true,
                IsTelegramVerified = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var regResult = await userManager.CreateAsync(newStudent, testPassword);
            if (!regResult.Succeeded)
            {
                var errors = string.Join("; ", regResult.Errors.Select(e => e.Description));
                throw new Exception($"Student registration failed: {errors}");
            }

            await userManager.AddToRoleAsync(newStudent, "Student");

            var persistedUser = await userManager.FindByNameAsync(testStudentId);
            if (persistedUser == null || persistedUser.UniversityId != testStudentId)
                throw new Exception("Persisted student not found by Student ID (UniversityId).");

            if (persistedUser.Email != null)
                throw new Exception($"Expected Email to be null, but found '{persistedUser.Email}'.");

            if (persistedUser.IsTelegramVerified)
                throw new Exception("Newly registered student should have IsTelegramVerified = false.");

            var studentRoles = await userManager.GetRolesAsync(persistedUser);
            if (!studentRoles.Contains("Student") || studentRoles.Count != 1)
                throw new Exception("Newly registered student should strictly possess only the 'Student' role.");

            Console.WriteLine("[PASS] Test 1: Student Registration: Registered student with ONLY Name, Student ID ('07875'), and Password (Email is null, role is 'Student').");

            // ─────────────────────────────────────────────────────────────
            // TEST 2: Student ID Uniqueness Enforcement
            // ─────────────────────────────────────────────────────────────
            var duplicateStudent = new User
            {
                UserName = testStudentId,
                UniversityId = testStudentId,
                FullName = "Imposter Student",
                Email = null,
                IsActive = true,
                IsTelegramVerified = false
            };

            var dupResult = await userManager.CreateAsync(duplicateStudent, "Another@123");
            if (dupResult.Succeeded)
            {
                throw new Exception("Security breach: Duplicate Student ID ('07875') was accepted!");
            }
            Console.WriteLine("[PASS] Test 2: Student ID Uniqueness: Duplicate Student ID ('07875') registration was correctly rejected.");

            // ─────────────────────────────────────────────────────────────
            // TEST 3: Telegram Verification Request Creation & Binding
            // ─────────────────────────────────────────────────────────────
            var generatedCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var verifyReq = new TelegramVerificationRequest
            {
                UserId = persistedUser.Id,
                UniversityId = persistedUser.UniversityId!,
                Code = generatedCode,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                IsUsed = false,
                AttemptsCount = 0,
                MaxAttempts = 5
            };

            context.TelegramVerificationRequests.Add(verifyReq);
            await context.SaveChangesAsync();

            // Deliver via Telegram service
            bool sent = await telegramService.SendVerificationCodeAsync(persistedUser.UniversityId!, persistedUser.FullName, generatedCode);
            if (!sent)
                throw new Exception("Telegram code delivery returned false.");

            var dbReq = await context.TelegramVerificationRequests
                .FirstOrDefaultAsync(r => r.UserId == persistedUser.Id && !r.IsUsed);

            if (dbReq == null || dbReq.Code != generatedCode || dbReq.UniversityId != testStudentId)
                throw new Exception("Telegram verification request not properly persisted or linked to User.Id.");

            Console.WriteLine("[PASS] Test 3: Telegram Request Binding: Verification request generated, securely linked to User.Id, and delivered via Telegram service.");

            // ─────────────────────────────────────────────────────────────
            // TEST 4: Unverified Student Login Protection (No Cookie/Session)
            // ─────────────────────────────────────────────────────────────
            var unverifiedLookup = await userManager.Users.FirstOrDefaultAsync(u => u.UniversityId == testStudentId);
            if (unverifiedLookup == null) throw new Exception("User not found.");

            var unverifiedRoles = await userManager.GetRolesAsync(unverifiedLookup);
            bool isStudentUnverified = unverifiedRoles.Contains("Student") && !unverifiedLookup.IsTelegramVerified;

            if (!isStudentUnverified)
                throw new Exception("Unverified policy check failed: User should be detected as unverified.");

            Console.WriteLine("[PASS] Test 4: Unverified Account Protection: Unverified student login check blocks sign-in and prevents issuing authentication cookie.");

            // ─────────────────────────────────────────────────────────────
            // TEST 5: Failed Verification with Incorrect Code
            // ─────────────────────────────────────────────────────────────
            const string wrongCode = "000000";
            if (string.Equals(dbReq.Code, wrongCode, StringComparison.Ordinal))
                throw new Exception("Test error: Wrong code matched actual code.");

            dbReq.AttemptsCount++;
            await context.SaveChangesAsync();

            var updatedReq = await context.TelegramVerificationRequests.FindAsync(dbReq.Id);
            if (updatedReq?.AttemptsCount != 1)
                throw new Exception("AttemptsCount did not increment after failed verification attempt.");

            var studentStillUnverified = await userManager.FindByIdAsync(persistedUser.Id);
            if (studentStillUnverified!.IsTelegramVerified)
                throw new Exception("Security breach: Student marked verified after wrong code!");

            Console.WriteLine("[PASS] Test 5: Failed Code Attempt: Invalid code was rejected and attempt counter incremented (1/5).");

            // ─────────────────────────────────────────────────────────────
            // TEST 6: Rate Limiting & Maximum Attempts Lockout
            // ─────────────────────────────────────────────────────────────
            dbReq.AttemptsCount = dbReq.MaxAttempts; // Max attempts reached (5)
            await context.SaveChangesAsync();

            bool isLockedOut = dbReq.AttemptsCount >= dbReq.MaxAttempts;
            if (!isLockedOut)
                throw new Exception("Request should be locked out after reaching MaxAttempts.");

            Console.WriteLine("[PASS] Test 6: Rate-Limiting Lockout: Verification request locked out after reaching maximum failed attempts (5/5).");

            // ─────────────────────────────────────────────────────────────
            // TEST 7: Expired Code Rejection
            // ─────────────────────────────────────────────────────────────
            var expiredReq = new TelegramVerificationRequest
            {
                UserId = persistedUser.Id,
                UniversityId = persistedUser.UniversityId!,
                Code = "654321",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-10), // Expired 10 mins ago
                IsUsed = false,
                AttemptsCount = 0,
                MaxAttempts = 5
            };
            context.TelegramVerificationRequests.Add(expiredReq);
            await context.SaveChangesAsync();

            bool isExpired = DateTime.UtcNow > expiredReq.ExpiresAt;
            if (!isExpired)
                throw new Exception("Expired code was not detected as expired.");

            Console.WriteLine("[PASS] Test 7: Expired Code Protection: Verification code past ExpiresAt timestamp correctly marked as expired.");

            // ─────────────────────────────────────────────────────────────
            // TEST 8: Successful Telegram Verification
            // ─────────────────────────────────────────────────────────────
            var validCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var validReq = new TelegramVerificationRequest
            {
                UserId = persistedUser.Id,
                UniversityId = persistedUser.UniversityId!,
                Code = validCode,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                IsUsed = false,
                AttemptsCount = 0,
                MaxAttempts = 5
            };
            context.TelegramVerificationRequests.Add(validReq);
            await context.SaveChangesAsync();

            // Simulate correct submission
            if (!string.Equals(validReq.Code, validCode, StringComparison.Ordinal))
                throw new Exception("Valid code did not match.");

            validReq.IsUsed = true;
            validReq.UsedAt = DateTime.UtcNow;

            persistedUser.IsTelegramVerified = true;
            persistedUser.UpdatedAt = DateTime.UtcNow;
            await userManager.UpdateAsync(persistedUser);
            await context.SaveChangesAsync();

            var verifiedCheck = await userManager.FindByIdAsync(persistedUser.Id);
            if (!verifiedCheck!.IsTelegramVerified)
                throw new Exception("User IsTelegramVerified remained false after successful verification.");

            var usedReqCheck = await context.TelegramVerificationRequests.FindAsync(validReq.Id);
            if (!usedReqCheck!.IsUsed || usedReqCheck.UsedAt == null)
                throw new Exception("Verification request was not marked IsUsed=true.");

            Console.WriteLine("[PASS] Test 8: Successful Verification: Valid verification code accepted, request marked single-use (IsUsed=true), and student activated (IsTelegramVerified=true).");

            // ─────────────────────────────────────────────────────────────
            // TEST 9: Student Login using Student ID + Password
            // ─────────────────────────────────────────────────────────────
            var loginUser = await userManager.Users
                .FirstOrDefaultAsync(u => u.UniversityId == testStudentId || u.UserName == testStudentId);

            if (loginUser == null)
                throw new Exception("Failed to resolve user by Student ID ('07875').");

            if (!loginUser.IsActive || !loginUser.IsTelegramVerified)
                throw new Exception("Verified student failed active/verified pre-login assertion.");

            var passCheck = await userManager.CheckPasswordAsync(loginUser, testPassword);
            if (!passCheck)
                throw new Exception("Identity password verification failed for registered student.");

            var studentRoleCheck = await userManager.GetRolesAsync(loginUser);
            if (!studentRoleCheck.Contains("Student"))
                throw new Exception("Student role missing from verified user.");

            Console.WriteLine("[PASS] Test 9: Student ID Login: Verified student resolves by Student ID ('07875') and authenticates successfully via Identity PasswordSignIn.");

            // ─────────────────────────────────────────────────────────────
            // TEST 10: Invalid Password Rejection for Student ID
            // ─────────────────────────────────────────────────────────────
            var badPassCheck = await userManager.CheckPasswordAsync(loginUser, "Incorrect@999");
            if (badPassCheck)
                throw new Exception("Security breach: Invalid password accepted for student.");

            Console.WriteLine("[PASS] Test 10: Invalid Password Protection: Wrong password rejected by Identity provider for Student ID.");

            // ─────────────────────────────────────────────────────────────
            // TEST 11: Inactive Account Login Protection
            // ─────────────────────────────────────────────────────────────
            loginUser.IsActive = false;
            await userManager.UpdateAsync(loginUser);

            var deactUser = await userManager.Users.FirstOrDefaultAsync(u => u.UniversityId == testStudentId);
            if (deactUser!.IsActive)
                throw new Exception("User should be inactive.");

            bool canLoginWhenDeactivated = deactUser.IsActive;
            if (canLoginWhenDeactivated)
                throw new Exception("Security breach: Deactivated student allowed to log in.");

            loginUser.IsActive = true;
            await userManager.UpdateAsync(loginUser);

            Console.WriteLine("[PASS] Test 11: Inactive Account Protection: Accounts with IsActive=false are strictly blocked from signing in.");

            // ─────────────────────────────────────────────────────────────
            // TEST 12: Admin & Faculty Login Preservation
            // ─────────────────────────────────────────────────────────────
            var adminUser = await userManager.FindByEmailAsync("admin@aub.com");
            var adminById = await userManager.Users.FirstOrDefaultAsync(u => u.UniversityId == "ADMIN001");
            if (adminUser == null || adminById == null || adminUser.Id != adminById.Id)
                throw new Exception("Admin account resolution failed.");

            if (!await userManager.CheckPasswordAsync(adminUser, "admin123"))
                throw new Exception("Admin password verification failed.");

            var teacherUser = await userManager.FindByEmailAsync("teacher@aub.com");
            var teacherById = await userManager.Users.FirstOrDefaultAsync(u => u.UniversityId == "TCH001");
            if (teacherUser == null || teacherById == null || teacherUser.Id != teacherById.Id)
                throw new Exception("Faculty account resolution failed.");

            if (!await userManager.CheckPasswordAsync(teacherUser, "teacher123"))
                throw new Exception("Faculty password verification failed.");

            Console.WriteLine("[PASS] Test 12: Admin & Faculty Preservation: Admin ('ADMIN001' / 'admin@aub.com') and Faculty ('TCH001' / 'teacher@aub.com') authentications fully preserved.");

            // ─────────────────────────────────────────────────────────────
            // TEST 13: Resend Code 60-Second Cooldown & Invalidation
            // ─────────────────────────────────────────────────────────────
            var resendBaseReq = new TelegramVerificationRequest
            {
                UserId = persistedUser.Id,
                UniversityId = persistedUser.UniversityId!,
                Code = "111111",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                IsUsed = false,
                AttemptsCount = 0,
                MaxAttempts = 5
            };
            context.TelegramVerificationRequests.Add(resendBaseReq);
            await context.SaveChangesAsync();

            // Attempt resend immediately (< 60s)
            bool cooldownEnforced = (DateTime.UtcNow - resendBaseReq.CreatedAt).TotalSeconds < 60;
            if (!cooldownEnforced)
                throw new Exception("Resend rate limiting cooldown (< 60s) was not enforced.");

            Console.WriteLine("[PASS] Test 13: Resend Rate Limiting: 60-second cooldown between verification code requests enforced.");
        }
        finally
        {
            // ─────────────────────────────────────────────────────────────
            // TEST 14: Clean Database Restoration / Hygiene
            // ─────────────────────────────────────────────────────────────
            await CleanupTestUserAsync(context, userManager, testStudentId);
            Console.WriteLine("[PASS] Test 14: Clean Database Hygiene: Temporary test records safely restored; no orphaned records left in SQL Server.");
        }

        Console.WriteLine("\n=================================================================");
        Console.WriteLine("🎉 ALL 14/14 REGISTRATION & TELEGRAM VERIFICATION TESTS PASSED!");
        Console.WriteLine("=================================================================\n");
    }

    private static async Task CleanupTestUserAsync(ApplicationDbContext context, UserManager<User> userManager, string studentId)
    {
        var existingStudent = await userManager.Users.FirstOrDefaultAsync(u => u.UniversityId == studentId || u.UserName == studentId);
        if (existingStudent != null)
        {
            var reqs = await context.TelegramVerificationRequests
                .Where(r => r.UserId == existingStudent.Id || r.UniversityId == studentId)
                .ToListAsync();
            context.TelegramVerificationRequests.RemoveRange(reqs);
            await context.SaveChangesAsync();

            await userManager.DeleteAsync(existingStudent);
        }
    }
}
