using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <inheritdoc />
    public partial class AddJobApprovalAndReviewSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Reviews",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByAdminId",
                table: "Reviews",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "Reviews",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Reviews",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "JobPostings",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "JobPostings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByAdminId",
                table: "JobPostings",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedForApprovalAt",
                table: "JobPostings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "Employers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivatedByAdminId",
                table: "Employers",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DeactivationReason",
                table: "Employers",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Employers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "CompanyProfiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivatedByAdminId",
                table: "CompanyProfiles",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "CompanyProfiles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_DeletedByAdminId",
                table: "Reviews",
                column: "DeletedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_ReviewedByAdminId",
                table: "JobPostings",
                column: "ReviewedByAdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobPostings_AspNetUsers_ReviewedByAdminId",
                table: "JobPostings",
                column: "ReviewedByAdminId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_AspNetUsers_DeletedByAdminId",
                table: "Reviews",
                column: "DeletedByAdminId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobPostings_AspNetUsers_ReviewedByAdminId",
                table: "JobPostings");

            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_AspNetUsers_DeletedByAdminId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_DeletedByAdminId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_JobPostings_ReviewedByAdminId",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "DeletedByAdminId",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "ReviewedByAdminId",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "SubmittedForApprovalAt",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Employers");

            migrationBuilder.DropColumn(
                name: "DeactivatedByAdminId",
                table: "Employers");

            migrationBuilder.DropColumn(
                name: "DeactivationReason",
                table: "Employers");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Employers");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "DeactivatedByAdminId",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "CompanyProfiles");
        }
    }
}
