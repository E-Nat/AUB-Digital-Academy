import os
import shutil
import glob
import re

base_dir = r"C:\Users\E-Nat\.gemini\antigravity\scratch\AUBDigitalAcademy"

# 1. Rename AuthController to AccountController
auth_ctrl = os.path.join(base_dir, "Controllers", "AuthController.cs")
acct_ctrl = os.path.join(base_dir, "Controllers", "AccountController.cs")
if os.path.exists(auth_ctrl):
    os.rename(auth_ctrl, acct_ctrl)

# 2. Move ViewModels to Models/ViewModels
old_vm_dir = os.path.join(base_dir, "ViewModels")
new_vm_dir = os.path.join(base_dir, "Models", "ViewModels")
if os.path.exists(old_vm_dir):
    shutil.move(old_vm_dir, new_vm_dir)

# Flatten Models/ViewModels/Auth into Models/ViewModels
auth_vms = os.path.join(new_vm_dir, "Auth")
if os.path.exists(auth_vms):
    for f in os.listdir(auth_vms):
        shutil.move(os.path.join(auth_vms, f), os.path.join(new_vm_dir, f))
    os.rmdir(auth_vms)

# 3. Rename Views/Auth to Views/Account
old_views = os.path.join(base_dir, "Views", "Auth")
new_views = os.path.join(base_dir, "Views", "Account")
if os.path.exists(old_views):
    os.rename(old_views, new_views)

# 4. Rename Models
app_user = os.path.join(base_dir, "Models", "ApplicationUser.cs")
user_model = os.path.join(base_dir, "Models", "User.cs")
if os.path.exists(app_user):
    os.rename(app_user, user_model)

mat_model = os.path.join(base_dir, "Models", "Material.cs")
book_model = os.path.join(base_dir, "Models", "Book.cs")
if os.path.exists(mat_model):
    os.rename(mat_model, book_model)

# 5. Create empty controllers
controllers = ["AdminController.cs", "TeacherController.cs", "StudentController.cs", 
               "DashboardController.cs", "UserController.cs", "AcademicController.cs", 
               "EnrollmentController.cs", "SettingsController.cs"]
for c in controllers:
    path = os.path.join(base_dir, "Controllers", c)
    if not os.path.exists(path):
        with open(path, "w") as f:
            f.write(f"// {c}\nnamespace AUBDigitalAcademy.Controllers;\n\npublic class {c.replace('.cs', '')} : Microsoft.AspNetCore.Mvc.Controller {{ }}")

# 6. Create Services Layer
services_dir = os.path.join(base_dir, "Services")
interfaces_dir = os.path.join(services_dir, "Interfaces")
impl_dir = os.path.join(services_dir, "Implementations")
os.makedirs(interfaces_dir, exist_ok=True)
os.makedirs(impl_dir, exist_ok=True)

for i in ["IUserService.cs", "ICourseService.cs", "IEnrollmentService.cs", "IGoogleAuthService.cs"]:
    with open(os.path.join(interfaces_dir, i), "w") as f:
        f.write(f"// {i}\nnamespace AUBDigitalAcademy.Services.Interfaces;\n\npublic interface {i.replace('.cs', '')} {{ }}")

for i in ["UserService.cs", "CourseService.cs", "EnrollmentService.cs", "GoogleAuthService.cs"]:
    with open(os.path.join(impl_dir, i), "w") as f:
        f.write(f"// {i}\nnamespace AUBDigitalAcademy.Services.Implementations;\n\npublic class {i.replace('.cs', '')} : AUBDigitalAcademy.Services.Interfaces.I{i.replace('.cs', '')} {{ }}")

# 7. Create empty Views
views_dirs = {
    "Dashboard": ["Index.cshtml"],
    "User": ["Index.cshtml", "Create.cshtml", "Edit.cshtml", "Details.cshtml"],
    "Academic": ["Category.cshtml", "Major.cshtml", "Semester.cshtml", "Course.cshtml", "Chapter.cshtml", "CourseItem.cshtml"],
    "Enrollment": ["Index.cshtml", "Create.cshtml", "Edit.cshtml"],
    "Teacher": ["Dashboard.cshtml", "Courses.cshtml", "Books.cshtml", "Quizzes.cshtml", "Assignments.cshtml", "Grades.cshtml"],
    "Student": ["Dashboard.cshtml", "MyCourses.cshtml", "Book.cshtml", "Quiz.cshtml", "Assignment.cshtml", "Grades.cshtml"],
    "Settings": ["Profile.cshtml", "ChangePassword.cshtml"],
    "Account": ["ResetPassword.cshtml"] # add missing from requested list
}

for folder, files in views_dirs.items():
    folder_path = os.path.join(base_dir, "Views", folder)
    os.makedirs(folder_path, exist_ok=True)
    for f in files:
        file_path = os.path.join(folder_path, f)
        if not os.path.exists(file_path):
            with open(file_path, "w") as fw:
                fw.write(f"<!-- {f} -->\n")

# Layouts
shared_dir = os.path.join(base_dir, "Views", "Shared")
for l in ["_AdminLayout.cshtml", "_TeacherLayout.cshtml", "_StudentLayout.cshtml", "_Navbar.cshtml", "_Sidebar.cshtml"]:
    with open(os.path.join(shared_dir, l), "w") as f:
        f.write(f"<!-- {l} -->\n")

# 8. Create empty ViewModels and Models
with open(os.path.join(base_dir, "Models", "Role.cs"), "w") as f:
    f.write("using Microsoft.AspNetCore.Identity;\nnamespace AUBDigitalAcademy.Models;\npublic class Role : IdentityRole { }")

for v in ["RegisterViewModel.cs", "DashboardViewModel.cs", "EnrollmentViewModel.cs"]:
    with open(os.path.join(new_vm_dir, v), "w") as f:
        f.write(f"namespace AUBDigitalAcademy.Models.ViewModels;\npublic class {v.replace('.cs', '')} {{ }}")

# 9. Create wwwroot folders and css/js placeholders
wwwroot = os.path.join(base_dir, "wwwroot")
os.makedirs(os.path.join(wwwroot, "images", "logo"), exist_ok=True)
os.makedirs(os.path.join(wwwroot, "images", "illustrations"), exist_ok=True)
os.makedirs(os.path.join(wwwroot, "images", "icons"), exist_ok=True)
os.makedirs(os.path.join(wwwroot, "images", "backgrounds"), exist_ok=True)
os.makedirs(os.path.join(wwwroot, "fonts"), exist_ok=True)
os.makedirs(os.path.join(wwwroot, "vendor"), exist_ok=True)

for css in ["dashboard.css", "login.css", "admin.css", "teacher.css", "student.css"]:
    open(os.path.join(wwwroot, "css", css), "a").close()

for js in ["dashboard.js", "validation.js", "chart.js"]:
    open(os.path.join(wwwroot, "js", js), "a").close()

with open(os.path.join(base_dir, "Data", "DbInitializer.cs"), "w") as f:
    f.write("namespace AUBDigitalAcademy.Data;\npublic class DbInitializer { }")


# 10. Bulk Find and Replace Content
def replace_in_files(dir_path, replacements, extensions=(".cs", ".cshtml", ".json")):
    for root, dirs, files in os.walk(dir_path):
        if "obj" in root or "bin" in root or ".git" in root or "Migrations" in root:
            continue
        for file in files:
            if file.endswith(extensions):
                file_path = os.path.join(root, file)
                try:
                    with open(file_path, "r", encoding="utf-8") as f:
                        content = f.read()
                    
                    new_content = content
                    for old, new in replacements.items():
                        new_content = new_content.replace(old, new)
                    
                    if content != new_content:
                        with open(file_path, "w", encoding="utf-8") as f:
                            f.write(new_content)
                except Exception as e:
                    print(f"Failed to process {file_path}: {e}")

replacements = {
    "ApplicationUser": "User",
    "AuthController": "AccountController",
    "Material": "Book",
    "ViewModels.Auth": "Models.ViewModels",
    "AUBDigitalAcademy.ViewModels": "AUBDigitalAcademy.Models.ViewModels",
    "Views/Auth": "Views/Account",
    "asp-controller=\"Auth\"": "asp-controller=\"Account\"",
    "RedirectToAction(\"Login\", \"Auth\")": "RedirectToAction(\"Login\", \"Account\")"
}

replace_in_files(base_dir, replacements)

print("Restructuring complete.")
