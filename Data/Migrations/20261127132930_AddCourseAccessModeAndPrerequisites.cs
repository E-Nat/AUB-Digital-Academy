using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AUBDigitalAcademy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseAccessModeAndPrerequisites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccessMode",
                table: "Courses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CourseItemPrerequisites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CourseItemId = table.Column<int>(type: "int", nullable: false),
                    RequiredCourseItemId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseItemPrerequisites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseItemPrerequisites_CourseItems_CourseItemId",
                        column: x => x.CourseItemId,
                        principalTable: "CourseItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseItemPrerequisites_CourseItems_RequiredCourseItemId",
                        column: x => x.RequiredCourseItemId,
                        principalTable: "CourseItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseItemPrerequisites_CourseItemId_RequiredCourseItemId",
                table: "CourseItemPrerequisites",
                columns: new[] { "CourseItemId", "RequiredCourseItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseItemPrerequisites_RequiredCourseItemId",
                table: "CourseItemPrerequisites",
                column: "RequiredCourseItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseItemPrerequisites");

            migrationBuilder.DropColumn(
                name: "AccessMode",
                table: "Courses");
        }
    }
}
