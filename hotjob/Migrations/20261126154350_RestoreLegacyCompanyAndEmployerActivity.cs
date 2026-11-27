using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <inheritdoc />
    public partial class RestoreLegacyCompanyAndEmployerActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Employers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "CompanyProfiles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: false);

            // The original migration filled existing rows with false. Restore only rows
            // without any admin-deactivation metadata; do not change approval/membership.
            migrationBuilder.Sql("""
                UPDATE `CompanyProfiles`
                SET `IsActive` = TRUE
                WHERE `IsActive` = FALSE
                  AND `DeactivatedAt` IS NULL
                  AND `DeactivatedByAdminId` IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE `Employers`
                SET `IsActive` = TRUE
                WHERE `IsActive` = FALSE
                  AND `DeactivatedAt` IS NULL
                  AND `DeactivatedByAdminId` IS NULL
                  AND `DeactivationReason` IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert schema defaults only. Repaired activity flags cannot safely be
            // inferred later; rolling back must not deactivate valid companies/employers.
            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Employers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "CompanyProfiles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: true);
        }
    }
}
