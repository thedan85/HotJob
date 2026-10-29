using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <inheritdoc />
    public partial class AddModelTrainingLogIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ModelTrainingLogs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ModelTrainingLogs_IsActive_TrainedAt",
                table: "ModelTrainingLogs",
                columns: new[] { "IsActive", "TrainedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModelTrainingLogs_IsActive_TrainedAt",
                table: "ModelTrainingLogs");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ModelTrainingLogs");
        }
    }
}
