using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <inheritdoc />
    public partial class AddJobModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ModeratedAt",
                table: "JobPostings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModeratedByAdminId",
                table: "JobPostings",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ModerationReason",
                table: "JobPostings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_ModeratedByAdminId",
                table: "JobPostings",
                column: "ModeratedByAdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobPostings_AspNetUsers_ModeratedByAdminId",
                table: "JobPostings",
                column: "ModeratedByAdminId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobPostings_AspNetUsers_ModeratedByAdminId",
                table: "JobPostings");

            migrationBuilder.DropIndex(
                name: "IX_JobPostings_ModeratedByAdminId",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "ModeratedAt",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "ModeratedByAdminId",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "ModerationReason",
                table: "JobPostings");
        }
    }
}
