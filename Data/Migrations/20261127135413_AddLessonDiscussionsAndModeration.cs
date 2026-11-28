using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AUBDigitalAcademy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonDiscussionsAndModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LessonDiscussionThreads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CourseItemId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    IsAnswered = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModerationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonDiscussionThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonDiscussionThreads_AspNetUsers_DeletedById",
                        column: x => x.DeletedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonDiscussionThreads_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonDiscussionThreads_CourseItems_CourseItemId",
                        column: x => x.CourseItemId,
                        principalTable: "CourseItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonDiscussionReplies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ThreadId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    IsInstructorAnswer = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModerationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonDiscussionReplies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonDiscussionReplies_AspNetUsers_DeletedById",
                        column: x => x.DeletedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonDiscussionReplies_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonDiscussionReplies_LessonDiscussionThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "LessonDiscussionThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonDiscussionReplies_DeletedById",
                table: "LessonDiscussionReplies",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_LessonDiscussionReplies_ThreadId",
                table: "LessonDiscussionReplies",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonDiscussionReplies_ThreadId_IsPinned_CreatedAt",
                table: "LessonDiscussionReplies",
                columns: new[] { "ThreadId", "IsPinned", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonDiscussionReplies_UserId",
                table: "LessonDiscussionReplies",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonDiscussionThreads_CourseItemId",
                table: "LessonDiscussionThreads",
                column: "CourseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonDiscussionThreads_CourseItemId_IsPinned_CreatedAt",
                table: "LessonDiscussionThreads",
                columns: new[] { "CourseItemId", "IsPinned", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonDiscussionThreads_DeletedById",
                table: "LessonDiscussionThreads",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_LessonDiscussionThreads_UserId",
                table: "LessonDiscussionThreads",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonDiscussionReplies");

            migrationBuilder.DropTable(
                name: "LessonDiscussionThreads");
        }
    }
}
