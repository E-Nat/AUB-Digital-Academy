$baseDir = "C:\Users\E-Nat\.gemini\antigravity\scratch\AUBDigitalAcademy"

# 1. Rename AuthController to AccountController
$authCtrl = Join-Path $baseDir "Controllers\AuthController.cs"
$acctCtrl = Join-Path $baseDir "Controllers\AccountController.cs"
if (Test-Path $authCtrl) { Rename-Item $authCtrl -NewName "AccountController.cs" }

# 2. Move ViewModels to Models/ViewModels
$oldVmDir = Join-Path $baseDir "ViewModels"
$newVmDir = Join-Path $baseDir "Models\ViewModels"
if (Test-Path $oldVmDir) {
    if (!(Test-Path $newVmDir)) { New-Item -ItemType Directory -Force -Path $newVmDir | Out-Null }
    # Move files inside Auth to ViewModels directly
    if (Test-Path "$oldVmDir\Auth") {
        Get-ChildItem "$oldVmDir\Auth" | Move-Item -Destination $newVmDir
        Remove-Item "$oldVmDir\Auth" -Force
    }
    Get-ChildItem $oldVmDir | Move-Item -Destination $newVmDir
    Remove-Item $oldVmDir -Force
}

# 3. Rename Views/Auth to Views/Account
$oldViews = Join-Path $baseDir "Views\Auth"
$newViews = Join-Path $baseDir "Views\Account"
if (Test-Path $oldViews) { Rename-Item $oldViews -NewName "Account" }

# 4. Rename Models
$appUser = Join-Path $baseDir "Models\ApplicationUser.cs"
if (Test-Path $appUser) { Rename-Item $appUser -NewName "User.cs" }

$matModel = Join-Path $baseDir "Models\Material.cs"
if (Test-Path $matModel) { Rename-Item $matModel -NewName "Book.cs" }

# 5. Create empty controllers
$controllers = @("AdminController.cs", "TeacherController.cs", "StudentController.cs", "DashboardController.cs", "UserController.cs", "AcademicController.cs", "EnrollmentController.cs", "SettingsController.cs")
foreach ($c in $controllers) {
    $path = Join-Path $baseDir "Controllers\$c"
    if (!(Test-Path $path)) {
        $name = $c.Replace('.cs', '')
        Set-Content -Path $path -Value "// $c`nnamespace AUBDigitalAcademy.Controllers;`n`npublic class $name : Microsoft.AspNetCore.Mvc.Controller { }"
    }
}

# 6. Create Services Layer
$servicesDir = Join-Path $baseDir "Services"
$interfacesDir = Join-Path $servicesDir "Interfaces"
$implDir = Join-Path $servicesDir "Implementations"
New-Item -ItemType Directory -Force -Path $interfacesDir | Out-Null
New-Item -ItemType Directory -Force -Path $implDir | Out-Null

$interfaces = @("IUserService.cs", "ICourseService.cs", "IEnrollmentService.cs", "IGoogleAuthService.cs")
foreach ($i in $interfaces) {
    $name = $i.Replace('.cs', '')
    Set-Content -Path (Join-Path $interfacesDir $i) -Value "// $i`nnamespace AUBDigitalAcademy.Services.Interfaces;`n`npublic interface $name { }"
}

$impls = @("UserService.cs", "CourseService.cs", "EnrollmentService.cs", "GoogleAuthService.cs")
foreach ($i in $impls) {
    $name = $i.Replace('.cs', '')
    $iname = "I" + $name
    Set-Content -Path (Join-Path $implDir $i) -Value "// $i`nnamespace AUBDigitalAcademy.Services.Implementations;`n`npublic class $name : AUBDigitalAcademy.Services.Interfaces.$iname { }"
}

# 7. Create empty Views
$viewsDirs = @{
    "Dashboard" = @("Index.cshtml")
    "User" = @("Index.cshtml", "Create.cshtml", "Edit.cshtml", "Details.cshtml")
    "Academic" = @("Category.cshtml", "Major.cshtml", "Semester.cshtml", "Course.cshtml", "Chapter.cshtml", "CourseItem.cshtml")
    "Enrollment" = @("Index.cshtml", "Create.cshtml", "Edit.cshtml")
    "Teacher" = @("Dashboard.cshtml", "Courses.cshtml", "Books.cshtml", "Quizzes.cshtml", "Assignments.cshtml", "Grades.cshtml")
    "Student" = @("Dashboard.cshtml", "MyCourses.cshtml", "Book.cshtml", "Quiz.cshtml", "Assignment.cshtml", "Grades.cshtml")
    "Settings" = @("Profile.cshtml", "ChangePassword.cshtml")
    "Account" = @("ResetPassword.cshtml")
}

foreach ($key in $viewsDirs.Keys) {
    $folderPath = Join-Path $baseDir "Views\$key"
    New-Item -ItemType Directory -Force -Path $folderPath | Out-Null
    foreach ($f in $viewsDirs[$key]) {
        $filePath = Join-Path $folderPath $f
        if (!(Test-Path $filePath)) { Set-Content -Path $filePath -Value "<!-- $f -->" }
    }
}

$sharedDir = Join-Path $baseDir "Views\Shared"
$sharedFiles = @("_AdminLayout.cshtml", "_TeacherLayout.cshtml", "_StudentLayout.cshtml", "_Navbar.cshtml", "_Sidebar.cshtml")
foreach ($l in $sharedFiles) {
    Set-Content -Path (Join-Path $sharedDir $l) -Value "<!-- $l -->"
}

# 8. Create empty ViewModels and Models
Set-Content -Path (Join-Path $baseDir "Models\Role.cs") -Value "using Microsoft.AspNetCore.Identity;`nnamespace AUBDigitalAcademy.Models;`npublic class Role : IdentityRole { }"

$vms = @("RegisterViewModel.cs", "DashboardViewModel.cs", "EnrollmentViewModel.cs")
foreach ($v in $vms) {
    $name = $v.Replace('.cs', '')
    Set-Content -Path (Join-Path $newVmDir $v) -Value "namespace AUBDigitalAcademy.Models.ViewModels;`npublic class $name { }"
}

# 9. Create wwwroot folders and placeholders
$wwwroot = Join-Path $baseDir "wwwroot"
New-Item -ItemType Directory -Force -Path "$wwwroot\images\logo" | Out-Null
New-Item -ItemType Directory -Force -Path "$wwwroot\images\illustrations" | Out-Null
New-Item -ItemType Directory -Force -Path "$wwwroot\images\icons" | Out-Null
New-Item -ItemType Directory -Force -Path "$wwwroot\images\backgrounds" | Out-Null
New-Item -ItemType Directory -Force -Path "$wwwroot\fonts" | Out-Null
New-Item -ItemType Directory -Force -Path "$wwwroot\vendor" | Out-Null

@("dashboard.css", "login.css", "admin.css", "teacher.css", "student.css") | ForEach-Object {
    $path = "$wwwroot\css\$_"
    if (!(Test-Path $path)) { Set-Content -Path $path -Value "" }
}

@("dashboard.js", "validation.js", "chart.js") | ForEach-Object {
    $path = "$wwwroot\js\$_"
    if (!(Test-Path $path)) { Set-Content -Path $path -Value "" }
}

Set-Content -Path (Join-Path $baseDir "Data\DbInitializer.cs") -Value "namespace AUBDigitalAcademy.Data;`npublic class DbInitializer { }"
Set-Content -Path (Join-Path $baseDir "Data\SeedData.cs") -Value "namespace AUBDigitalAcademy.Data;`npublic static class SeedData { }"

# 10. Bulk Find and Replace Content
$replacements = @{
    "ApplicationUser" = "User"
    "AuthController" = "AccountController"
    "Material" = "Book"
    "ViewModels.Auth" = "Models.ViewModels"
    "AUBDigitalAcademy.ViewModels" = "AUBDigitalAcademy.Models.ViewModels"
    "Views/Auth" = "Views/Account"
    "asp-controller=`"Auth`"" = "asp-controller=`"Account`""
    "RedirectToAction(`"Login`", `"Auth`")" = "RedirectToAction(`"Login`", `"Account`")"
}

$filesToProcess = Get-ChildItem -Path $baseDir -Recurse -Include *.cs, *.cshtml, *.json | 
                  Where-Object { $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\bin\\" -and $_.FullName -notmatch "\\.git\\" -and $_.FullName -notmatch "\\Migrations\\" }

foreach ($file in $filesToProcess) {
    $content = Get-Content -Path $file.FullName -Raw
    if ($null -ne $content) {
        $newContent = $content
        foreach ($key in $replacements.Keys) {
            $newContent = $newContent.Replace($key, $replacements[$key])
        }
        if ($content -cne $newContent) {
            Set-Content -Path $file.FullName -Value $newContent -Encoding UTF8
        }
    }
}

Write-Host "Restructuring complete."
