AUB DIGITAL ACADEMY COMPLETE PROJECT FEATURES & REQUIREMENTS

 1. PROJECT OVERVIEW

Project: AUB Digital Academy Institution: ACLEDA University of Business
(AUB) Product type: Digital Learning Management System (DLMS)

Main goal: Provide one digital platform for academic structure
management, course organization, learning content delivery, assessments,
student submissions, grading, final grades, student progress, and
communication between students and faculty.

Primary user roles: 1. Admin 2. Teacher 3. Assistance / Teaching
Assistant (TA) 4. Student

Core functional domains: - Academic Structure - Learning Content -
Assessment - Users & Students - System Administration - Learning
Progress - Notifications and Communication

 2. USER ROLES AND PERMISSIONS

2.1 ADMIN

Admin manages the overall platform.

Expected capabilities: - Access the Admin dashboard. - Manage academic
categories and majors. - Manage academic semesters. - Manage courses and
curriculum mappings. - Manage sections and chapters. - Manage books and
learning content. - Manage course items. - Manage quizzes, questions,
answers, and assignments. - View and manage submissions and grading
workflows as authorized. - Manage users and roles. - Manage student
enrollments. - View student learning progress. - Manage or review final
grades. - View login history. - View reports and analytics. - Manage
system settings and lookup values. - Assign and unassign Teaching
Assistants to courses. - Manage attachment soft deletion and restoration
under Phase 4 rules. - View progress across courses under Phase 5
requirements. - Publish global announcements under Phase 5 requirements.

Security: - Admin-only actions must be enforced by backend
authorization, not just hidden buttons. - User and role management must
cover Admins, Teachers/Instructors, TAs, and Students.

2.2 TEACHER

Expected capabilities: - Access the shared Faculty Dashboard. - View and
manage courses owned by the Teacher. - Manage course sections and
chapters where authorized. - Add or manage books, book contents, and
course items. - Create and manage quizzes, questions, and answers. -
Create and manage assignments. - Review student submissions for owned
courses. - Grade quiz/assignment submissions according to permissions. -
Provide grading feedback. - Configure quiz maximum attempts for their
own courses under Phase 4 rules. - View learning progress for their own
courses under Phase 5 requirements. - Reply to lesson discussion
questions. - Pin helpful answers and mark discussions as answered where
authorized. - Publish course-scoped announcements under Phase 5
requirements.

Security: - Teachers may manage only their own courses. - Backend must
enforce ownership for every relevant route and API.

2.3 ASSISTANCE / TEACHING ASSISTANT (TA)

Expected capabilities: - Use the shared Faculty Dashboard with
role-specific actions. - Access only courses assigned by Admin. - View
assigned course content and student activity within assigned courses. -
Reply to lesson discussions under Phase 5 requirements. - Monitor
progress for assigned courses under Phase 5 requirements. - Grade only
when the TA has CanGrade=true.

Rules: - Only Admin can assign or unassign TAs. -
CourseAssistant.CanGrade defaults to false. - Grading requires both an
active course assignment and CanGrade=true. - Development/Test seed
previously specified ta@aub.com assigned to CS301 with CanGrade=true
only in Development/Test. - Do not automatically grant production
grading permission through seed data.

2.4 STUDENT

Expected capabilities: - Sign in and access the Student Dashboard. -
View enrolled courses. - Open course sections, chapters, books, and
course items permitted by course access rules. - Read course
materials. - Watch video lessons. - Resume video playback where the
provider supports it (Phase 5). - View personal video learning progress
(Phase 5). - Take quizzes within configured attempt limits. - Submit
assignments using supported attachments and/or external URLs. - Resubmit
assignments where allowed, preserving earlier attempts. - View grades
and feedback when released/authorized. - Ask questions and participate
in lesson discussions under Phase 5 rules. - Receive in-app
notifications and announcements under Phase 5 requirements. - View only
their own records and learning progress.

 3. ADMIN DASHBOARD AND ACADEMIC STRUCTURE

3.1 ADMIN DASHBOARD Purpose: Provide an overview of the academic
platform and quick access to management areas.

Detailed dashboard metrics and widgets should be verified against the
actual implementation. Do not assume unconfirmed metrics are
implemented.

3.2 CATEGORIES & MAJORS Related data: - CategoryType - Major

Purpose: Organize academic categories and majors.

Expected functions: - View categories and majors. - Create, edit, and
manage records according to business rules. - Associate majors with the
correct academic category/type. - Validate required fields and prevent
invalid relationships.

3.3 ACADEMIC SEMESTERS Related data: - Semester

Purpose: Manage academic semester records.

Expected functions: - View semester list. - Create and edit semester
information. - Select semesters when mapping courses to majors. -
Validate dates and required academic information according to existing
model rules.

3.4 COURSE MANAGEMENT Related data: - Courses - MajorCourse

Purpose: Manage courses and their academic curriculum placement.

Expected functions: - View, create, edit, and manage courses. - Assign
courses to specific majors. - Assign courses to specific semesters. -
Review the MajorCourse mapping. - Avoid duplicate or invalid curriculum
mappings.

Important relationship: MajorCourse links MajorID, CourseID, and
SemesterID. Course Management must provide this mapping or a dedicated
Curriculum / Major Courses tab.

3.5 SECTIONS & CHAPTERS Related data: - Sections - Chapters

Purpose: Organize a course into sections and chapters.

Expected functions: - Create and manage course sections. - Organize
chapters under the appropriate section/course structure. - Maintain
ordering and relationships. - Provide the structure used by learning
content and lesson navigation.

4.1 BOOKS & CONTENTS Related data: - Books - BookContents - Attachments
(where used)

Purpose: Provide structured learning materials for courses.

Expected functions: - Create and manage books or learning resources. -
Organize book contents. - Associate learning resources with the correct
course or curriculum structure. - Display content to permitted
students. - Manage related attachments in the context of the relevant
content.

4.2 COURSE ITEMS Related data: - CourseItems - ItemTypes

Purpose: Represent learning items associated with a course.

Expected functions: - Create and organize course items. - Associate the
correct item type. - Link items to relevant learning content or
assessment records according to the existing model. - Display items in
the course learning sequence.

ItemTypes is a system lookup/reference table. It should not necessarily
be a top-level sidebar menu; its management belongs under Settings /
System Lookups.

4.3 EXTERNAL VIDEO LESSONS Previously approved constraint: - Video
lessons use external video URLs. - Internal video upload or a
self-hosted media server is not included without separate approval.

Phase 5 adds playback progress tracking, completion, and resume behavior
where supported by the video provider.

 5.ASSESSMENT 

Assessment is divided into authoring and student submission/grading.

5.1 QUIZZES & QUESTIONS Related data: - Quizzes - Questions - Answers -
ViewAnswerType (lookup)

Expected functions: - Create and manage quizzes. - Create and manage
questions and answer options. - Support the question types already
present in the project. - Configure quiz settings, including maximum
attempts. - Associate quizzes with the appropriate course/course item. -
Present quizzes to eligible students. - Store student answers
relationally through QuizAnswers / approved answer entities. - Enforce
attempt limits on the backend.

Phase 4 quiz rules: - Default MaxAttempts = 3. - Preserve every
attempt. - Highest score is the official score. - Correct answers are
revealed only after all attempts are exhausted. - In-progress attempts
do not count as completed attempts for answer-reveal eligibility. -
Admin and Teacher can configure MaxAttempts. - Teacher can configure it
only for their own courses. - A changed attempt limit applies to future
students. - Students who already started retain their effective attempt
limit. - QuizSubmissions.MaxAttemptsAllowed stores the effective limit
for that student. - Passing threshold is 50%. - No letter grades or GPA
conversion.

5.2 ASSIGNMENTS Related data: - Assignments - AssSubmission -
AssignmentAttachments (Phase 4)

Expected functions: - Create and manage assignments. - Set assignment
instructions and due date using existing model capabilities. - Display
assignments to eligible students. - Accept student submissions. -
Support file attachments and external URLs. - Allow late submissions and
mark them Late, without a late penalty. - Allow resubmissions and
preserve all versions.

Phase 4 attachment and URL rules: - Maximum 5 attachments per
submission. - Maximum 10 MB per file. - Allowed file extensions: PDF,
ZIP, DOCX. - Validate file size, extension, content type, and file
signature. - Store files outside wwwroot. - Use GUID-based storage
filenames. - File and external URL may be included in the same
submission. - Both HTTP and HTTPS URLs are allowed. - Only the latest
attempt (IsLatest=true) can be graded. - Earlier attempts remain as
history. - Attachments are retained until explicit Admin deletion. -
Admin deletion is soft-delete, recoverable, and auditable. - Admin can
restore soft-deleted attachments. - No automatic age-based or permanent
deletion.

5.3 SUBMISSIONS & GRADING Related data: - QuizSubmission - QuizAnswers -
AssSubmission - AssignmentAttachments

Purpose: Allow faculty to review individual student assessment attempts
and assign scores.

Expected functions: - List submissions for authorized courses. - View
individual submission details and answers/files/URLs. - Grade
submissions. - Provide feedback. - Enforce Teacher ownership and TA
assignment/CanGrade permissions. - Preserve submission and attempt
history.

Assignment grading rule: - Only the latest assignment attempt may be
graded. - Earlier attempts are history only.

5.4 PASS / FAIL Approved rules: - Quiz pass threshold: 50%. - Assignment
pass threshold: 50%. - Display raw score, percentage, and Pass/Fail. -
Do not introduce A/B/C/F grades, GPA, or weighted grading.
 6. USERS,ENROLLMENTS, STUDENT PROGRESS, AND FINAL GRADES


6.1 USERS & ROLES Related data: - Users - Roles

Purpose: Manage accounts and access roles across the platform.

Expected functions: - View and manage user accounts. - Manage role
assignment according to authorization rules. - Cover Admin,
Teacher/Instructor, Assistance/TA, and Student accounts. - Do not place
all User Management under a student-only menu. - Enforce access
permissions on the backend.

Previously specified login identifiers: - University ID - Email -
Username

6.2 ENROLLMENTS Related data: - Enrollments

Purpose: Connect students to courses or academic offerings.

Expected functions: - View enrollment records. - Manage enrollment
according to existing business rules. - Ensure students access only
courses for which they are enrolled and authorized. - Preserve
relationships with course and student records.

6.3 STUDENT PROGRESS Related data: - Enrollments.Progress - SectionView
/ ChapterView - Phase 5 video progress records (planned)

Purpose: Show learning activity and completion status.

Phase 5 approved faculty roster fields: - Student identity - Completed
video lesson count - Progress percentage - Last activity - Lesson-level
detail

Visibility: - Admin: all courses. - Teacher: own courses. - TA: assigned
courses. - Student: own progress only.

Course video progress formula: Completed video lessons / Total video
lessons.

This is a count of completed video lessons, not total minutes watched.

6.4 FINAL GRADES Related data: - Grades (also referred to as tblGrades
in supplied analysis)

Purpose: Manage/view final course scores or transcript-related results.

Important distinction: Final Grades is separate from Submissions &
Grading: - Submissions & Grading handles individual quiz and assignment
attempts. - Final Grades handles final course scores/transcripts.

Do not assume weighted grades, GPA, or letter-grade conversion; these
were not approved in the Phase 4 grading rules.

 7. SYSTEM ADMINISTRATION


7.1 LOGIN HISTORY Related data: - LoginHistory

Purpose: Provide an administrative view of login history according to
existing logging and access policies.

7.2 REPORTS & ANALYTICS Purpose: Provide cross-table summaries and
operational/academic reporting.

Exact report list and metrics were not fully specified in the approved
requirements. Audit existing reports before adding new ones.

7.3 SETTINGS Purpose: Manage system configuration and lookup/reference
data.

Lookup tables identified: - ItemTypes - ViewAnswerType

These should be grouped under System Lookups or a similar Settings
subsection, not added as unnecessary top-level menu items.

8.VIDEO LEARNING PROGRESS


Status: Requirements approved; implementation not confirmed.

8.1 VIDEO WATCH TRACKING Track: - Qualifying watched playback - Watched
duration - Last playback position - Completion status - Activity
timestamps - Completion timestamps

Completion rule: - At least 90% qualifying watched playback is
required. - Seeking directly to 90% does not count. - The system must
distinguish actual watched playback from simply jumping forward. -
Resume playback where the external provider supports it.

Tracking scope: - Video lessons only. - Do not automatically extend this
rule to PDFs, quizzes, or assignments.

Historical audit findings to recheck: - YouTube iframe was reported
without enablejsapi=1 / YT.Player binding. - HTML5 video was reported
without required playback event listeners. - Existing CourseProgress was
reported as binary IsCompleted / CompletedAt. - A manual CompleteLesson
endpoint was reported as capable of completing a lesson without proof of
actual playback. These are historical audit findings, not a live
inspection.

8.2 COURSE ACCESS MODES Both modes are approved and configurable per
course:

FREE ACCESS - Students may open any published lesson permitted by
enrollment and authorization.

SEQUENTIAL ACCESS - Students must satisfy configured lesson
prerequisites before opening a locked lesson. - Teachers configure
prerequisites. - Explicit prerequisite relationships should support more
than one prerequisite. - Example: Lesson 4 can require both Lesson 2 and
Lesson 3. - Backend enforces access; hiding a link in the UI is not
sufficient. - Direct URL/API requests must not bypass the lock. -
Changing access settings must not erase existing progress.

Open implementation question: Sequential Access may include non-video
lessons, but the approved progress-tracking scope is video lessons only.
Decide what completion means for non-video items before implementing
their prerequisite behavior.

8.3 STUDENT LEARNING DASHBOARD Planned capabilities: - Show the
student’s enrolled learning context. - Show video lesson completion. -
Show course progress percentage. - Show last activity and lesson details
where designed. - Allow students to see their own progress.

8.4 FACULTY PROGRESS MONITORING Planned capabilities: - Admin can view
progress across all courses. - Teacher can view progress for own
courses. - TA can view progress for assigned courses. - Student can view
only own progress. - Faculty roster shows student identity, completed
count, percentage, last activity, and lesson details.
9. LESSON DISCUSSIONS

Status: Requirements approved in principle; implementation not
confirmed.
Planned discussion capabilities: - Discussion area under video
lessons. - Students can ask questions. - Students can reply (the precise
peer-reply policy still needs owner confirmation). - Teachers can
reply. - Assigned TAs can reply. - Faculty can pin helpful answers. -
Faculty can mark questions as answered. - Faculty can moderate or delete
inappropriate comments with authorization.

Open decision: The owner has not yet explicitly confirmed whether
students can reply to any student’s discussion thread or only to their
own threads. Do not silently choose a policy.
 10. NOTIFICATIONS AND ANNOUNCEMENTS

10.1 IN-APP NOTIFICATION CENTER Planned notification events include: -
Replies to lesson discussions. - New lessons. - Announcements. -
Released grades. - Course milestones.

Avoid noisy notifications for every video playback heartbeat.

10.2 ANNOUNCEMENTS - Admin can publish global announcements. - Teacher
can publish announcements scoped to their own courses. - Students should
receive/view announcements relevant to their access and enrollment. -
Backend must enforce who can publish and who can view.

11.AUTHENTICATION AND SECURITY
Authentication: - ASP.NET Core Identity. - Role-Based Access Control. -
Login identifiers previously specified: University ID, - Telegram two-factor authentication only. - Telegram bot:
@AUBLinkBot. - TelegramPollingService. - Single-use 6-digit 
Security requirements: - Enforce ownership, role, and course assignment
on the backend. - Do not rely on hiding navigation links as
authorization. - TAs must be assigned to a course. - TA grading requires
CanGrade=true. - Students may access only their own records and
permitted course content. - Validate uploaded files by size, extension,
content type, and signature. - Store uploads outside wwwroot. - Protect
secrets and credentials. - Keep audit trails for attachment
deletion/restoration.

