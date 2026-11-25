using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AUBDigitalAcademy.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_FinalRelationalAttachmentsAndQuizSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxAttemptsAllowed",
                table: "QuizSubmissions",
                type: "int",
                nullable: false,
                defaultValue: 3);

            // Backfill existing QuizSubmissions using the parent Quiz's MaxAttempts (defaulting to 3 if unspecified)
            migrationBuilder.Sql(@"
                UPDATE qs
                SET qs.[MaxAttemptsAllowed] = CASE WHEN q.[MaxAttempts] > 0 THEN q.[MaxAttempts] ELSE 3 END
                FROM [QuizSubmissions] qs
                INNER JOIN [Quizzes] q ON qs.[QuizId] = q.[Id];
            ");

            migrationBuilder.AddColumn<string>(
                name: "ExternalUrl",
                table: "AssignmentSubmissions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssignmentAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionId = table.Column<int>(type: "int", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DeletionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RestoredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RestoredById = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentAttachments_AspNetUsers_DeletedById",
                        column: x => x.DeletedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentAttachments_AspNetUsers_RestoredById",
                        column: x => x.RestoredById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentAttachments_AssignmentSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "AssignmentSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentAttachments_DeletedById",
                table: "AssignmentAttachments",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentAttachments_IsDeleted",
                table: "AssignmentAttachments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentAttachments_RestoredById",
                table: "AssignmentAttachments",
                column: "RestoredById");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentAttachments_SubmissionId",
                table: "AssignmentAttachments",
                column: "SubmissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentAttachments");

            migrationBuilder.DropColumn(
                name: "MaxAttemptsAllowed",
                table: "QuizSubmissions");

            migrationBuilder.DropColumn(
                name: "ExternalUrl",
                table: "AssignmentSubmissions");
        }
    }
}
