using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <inheritdoc />
    public partial class CompanyModerationRefinement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompanyReviewedAt",
                table: "JobPostings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyReviewedByEmployerId",
                table: "JobPostings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAt",
                table: "Employers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemovedByEmployerId",
                table: "Employers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivationReason",
                table: "CompanyProfiles",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyReviewedAt",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "CompanyReviewedByEmployerId",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "Employers");

            migrationBuilder.DropColumn(
                name: "RemovedByEmployerId",
                table: "Employers");

            migrationBuilder.DropColumn(
                name: "DeactivationReason",
                table: "CompanyProfiles");
        }
    }
}
