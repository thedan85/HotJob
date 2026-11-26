using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <summary>
    /// Repairs the data left by 20261018151155_AddJobApprovalAndReviewSoftDelete, which added
    /// CompanyProfiles.IsActive and Employers.IsActive with DEFAULT 0: every company and employer that
    /// already existed became "deactivated", hiding all of their jobs and locking out their employers.
    /// Every admin action that deactivates (company or employer deactivate, employer reject) also
    /// stamps DeactivatedAt, so a row with IsActive = 0 and no DeactivatedAt was never deactivated by
    /// anyone and is switched back on. Rows an admin really deactivated keep IsActive = 0.
    /// The column default becomes 1 so raw SQL inserts start active, like rows created through EF.
    /// </summary>
    public partial class BackfillCompanyEmployerIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE `CompanyProfiles` SET `IsActive` = 1 WHERE `IsActive` = 0 AND `DeactivatedAt` IS NULL;");
            migrationBuilder.Sql(
                "UPDATE `Employers` SET `IsActive` = 1 WHERE `IsActive` = 0 AND `DeactivatedAt` IS NULL;");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Employers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "CompanyProfiles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");
        }

        /// <inheritdoc />
        /// <remarks>Only the column default is reverted; re-hiding repaired rows would recreate the bug.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Employers",
                type: "tinyint(1)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "CompanyProfiles",
                type: "tinyint(1)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: true);
        }
    }
}
