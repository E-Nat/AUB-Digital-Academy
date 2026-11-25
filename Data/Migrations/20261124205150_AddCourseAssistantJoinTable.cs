using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AUBDigitalAcademy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseAssistantJoinTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseAssistants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CourseId = table.Column<int>(type: "int", nullable: false),
                    AssistantId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CanGrade = table.Column<bool>(type: "bit", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseAssistants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseAssistants_AspNetUsers_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseAssistants_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseAssistants_AssistantId",
                table: "CourseAssistants",
                column: "AssistantId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseAssistants_CourseId_AssistantId",
                table: "CourseAssistants",
                columns: new[] { "CourseId", "AssistantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseAssistants");
        }
    }
}
