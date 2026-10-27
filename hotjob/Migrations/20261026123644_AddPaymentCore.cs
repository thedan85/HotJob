using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hotjob.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FeaturedUntil",
                table: "JobPostings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanExpiresAt",
                table: "CompanyProfiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanId",
                table: "CompanyProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanStartedAt",
                table: "CompanyProfiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BoostUntil",
                table: "Candidates",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PremiumPlanId",
                table: "Candidates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PremiumStartedAt",
                table: "Candidates",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PremiumUntil",
                table: "Candidates",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Audience = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Price = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    JobQuota = table.Column<int>(type: "int", nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PayerUserId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompanyProfileId = table.Column<int>(type: "int", nullable: true),
                    CandidateId = table.Column<int>(type: "int", nullable: true),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: true),
                    JobPostingId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    VnpTxnRef = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VnpTransactionNo = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VnpResponseCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VnpBankCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VnpPayDate = table.Column<string>(type: "varchar(14)", maxLength: 14, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_AspNetUsers_PayerUserId",
                        column: x => x.PayerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_CompanyProfiles_CompanyProfileId",
                        column: x => x.CompanyProfileId,
                        principalTable: "CompanyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_JobPostings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalTable: "JobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Payments_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_FeaturedUntil",
                table: "JobPostings",
                column: "FeaturedUntil");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfiles_PlanId",
                table: "CompanyProfiles",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_PremiumPlanId",
                table: "Candidates",
                column: "PremiumPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CandidateId",
                table: "Payments",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CompanyProfileId",
                table: "Payments",
                column: "CompanyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_JobPostingId",
                table: "Payments",
                column: "JobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PayerUserId",
                table: "Payments",
                column: "PayerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PlanId",
                table: "Payments",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_PaidAt",
                table: "Payments",
                columns: new[] { "Status", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_VnpTxnRef",
                table: "Payments",
                column: "VnpTxnRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code",
                table: "Plans",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_Plans_PremiumPlanId",
                table: "Candidates",
                column: "PremiumPlanId",
                principalTable: "Plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyProfiles_Plans_PlanId",
                table: "CompanyProfiles",
                column: "PlanId",
                principalTable: "Plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_Plans_PremiumPlanId",
                table: "Candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_CompanyProfiles_Plans_PlanId",
                table: "CompanyProfiles");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_JobPostings_FeaturedUntil",
                table: "JobPostings");

            migrationBuilder.DropIndex(
                name: "IX_CompanyProfiles_PlanId",
                table: "CompanyProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_PremiumPlanId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "FeaturedUntil",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "PlanExpiresAt",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "PlanStartedAt",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "BoostUntil",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "PremiumPlanId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "PremiumStartedAt",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "PremiumUntil",
                table: "Candidates");
        }
    }
}
