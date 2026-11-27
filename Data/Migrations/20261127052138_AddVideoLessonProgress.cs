using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AUBDigitalAcademy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoLessonProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoLessonProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CourseItemId = table.Column<int>(type: "int", nullable: false),
                    QualifyingWatchedSeconds = table.Column<double>(type: "float", nullable: false),
                    WatchedDurationSeconds = table.Column<double>(type: "float", nullable: false),
                    LastPositionSeconds = table.Column<double>(type: "float", nullable: false),
                    VideoDurationSeconds = table.Column<double>(type: "float", nullable: false),
                    WatchedIntervalsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoLessonProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoLessonProgresses_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VideoLessonProgresses_CourseItems_CourseItemId",
                        column: x => x.CourseItemId,
                        principalTable: "CourseItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoLessonProgresses_CourseItemId",
                table: "VideoLessonProgresses",
                column: "CourseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoLessonProgresses_StudentId_CourseItemId",
                table: "VideoLessonProgresses",
                columns: new[] { "StudentId", "CourseItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoLessonProgresses");
        }
    }
}
