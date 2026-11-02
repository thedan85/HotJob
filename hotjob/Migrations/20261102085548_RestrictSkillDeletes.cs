using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <inheritdoc />
    public partial class RestrictSkillDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobSkills_Skills_SkillId",
                table: "JobSkills");

            migrationBuilder.DropForeignKey(
                name: "FK_ResumeSkills_Skills_SkillId",
                table: "ResumeSkills");

            migrationBuilder.AddForeignKey(
                name: "FK_JobSkills_Skills_SkillId",
                table: "JobSkills",
                column: "SkillId",
                principalTable: "Skills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ResumeSkills_Skills_SkillId",
                table: "ResumeSkills",
                column: "SkillId",
                principalTable: "Skills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobSkills_Skills_SkillId",
                table: "JobSkills");

            migrationBuilder.DropForeignKey(
                name: "FK_ResumeSkills_Skills_SkillId",
                table: "ResumeSkills");

            migrationBuilder.AddForeignKey(
                name: "FK_JobSkills_Skills_SkillId",
                table: "JobSkills",
                column: "SkillId",
                principalTable: "Skills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ResumeSkills_Skills_SkillId",
                table: "ResumeSkills",
                column: "SkillId",
                principalTable: "Skills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
