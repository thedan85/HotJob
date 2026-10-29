using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAlertNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Notifications",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "RelatedJobPostingId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedJobPostingId",
                table: "Notifications",
                column: "RelatedJobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Kind_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Kind_RelatedJobPostingId",
                table: "Notifications",
                columns: new[] { "UserId", "Kind", "RelatedJobPostingId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_JobPostings_RelatedJobPostingId",
                table: "Notifications",
                column: "RelatedJobPostingId",
                principalTable: "JobPostings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_JobPostings_RelatedJobPostingId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RelatedJobPostingId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_Kind_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_Kind_RelatedJobPostingId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RelatedJobPostingId",
                table: "Notifications");
        }
    }
}
