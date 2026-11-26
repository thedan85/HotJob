using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPostingStatusExpiryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_Status_ExpiryDate",
                table: "JobPostings",
                columns: new[] { "Status", "ExpiryDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobPostings_Status_ExpiryDate",
                table: "JobPostings");
        }
    }
}
