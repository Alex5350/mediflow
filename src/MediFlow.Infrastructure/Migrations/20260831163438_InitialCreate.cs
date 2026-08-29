using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace MediFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EntityKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BenefitAccumulators",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    BenefitYear = table.Column<int>(type: "int", nullable: false),
                    DeductibleMetCents = table.Column<int>(type: "int", nullable: false),
                    OopMetCents = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenefitAccumulators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Members",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Mbi = table.Column<string>(type: "nchar(11)", fixedLength: true, maxLength: 11, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    StateCode = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    PartAEffective = table.Column<DateOnly>(type: "date", nullable: true),
                    PartBEffective = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeasedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Carrier = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ContractYear = table.Column<int>(type: "int", nullable: false),
                    MonthlyPremiumCents = table.Column<int>(type: "int", nullable: false),
                    DeductibleCents = table.Column<int>(type: "int", nullable: false),
                    CoinsurancePercent = table.Column<byte>(type: "tinyint", nullable: false),
                    OopMaxCents = table.Column<int>(type: "int", nullable: false),
                    IsFiveStar = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcedureFees",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcedureCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    AllowedCents = table.Column<int>(type: "int", nullable: false),
                    IsCovered = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveYear = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureFees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Claims",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    EnrollmentApplicationId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    RenderingProviderNpi = table.Column<string>(type: "nchar(10)", fixedLength: true, maxLength: 10, nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalChargeCents = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AdjudicatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalAllowedCents = table.Column<int>(type: "int", nullable: true),
                    TotalPlanPaidCents = table.Column<int>(type: "int", nullable: true),
                    TotalMemberOwesCents = table.Column<int>(type: "int", nullable: true),
                    DenialCode = table.Column<int>(type: "int", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Claims_Members_MemberId",
                        column: x => x.MemberId,
                        principalSchema: "dbo",
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Claims_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "dbo",
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Enrollments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SepReason = table.Column<int>(type: "int", nullable: false),
                    RequestedEffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancelledEffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Enrollments_Members_MemberId",
                        column: x => x.MemberId,
                        principalSchema: "dbo",
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Enrollments_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "dbo",
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClaimLines",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ProcedureCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    ChargeCents = table.Column<int>(type: "int", nullable: false),
                    AllowedCents = table.Column<int>(type: "int", nullable: true),
                    PlanPaidCents = table.Column<int>(type: "int", nullable: true),
                    MemberOwesCents = table.Column<int>(type: "int", nullable: true),
                    DenialCode = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimLines_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalSchema: "dbo",
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityKey_AtUtc",
                schema: "dbo",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityKey", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BenefitAccumulators_MemberId_BenefitYear",
                schema: "dbo",
                table: "BenefitAccumulators",
                columns: new[] { "MemberId", "BenefitYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimLines_ClaimId_Sequence",
                schema: "dbo",
                table: "ClaimLines",
                columns: new[] { "ClaimId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ClaimNumber",
                schema: "dbo",
                table: "Claims",
                column: "ClaimNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Claims_MemberId",
                schema: "dbo",
                table: "Claims",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_MemberId_ServiceDate",
                schema: "dbo",
                table: "Claims",
                columns: new[] { "MemberId", "ServiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_PlanId",
                schema: "dbo",
                table: "Claims",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_Queue",
                schema: "dbo",
                table: "Claims",
                columns: new[] { "Status", "ReceivedAtUtc" },
                filter: "[Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_RenderingProviderNpi",
                schema: "dbo",
                table: "Claims",
                column: "RenderingProviderNpi");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_ApplicationNumber",
                schema: "dbo",
                table: "Enrollments",
                column: "ApplicationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_MemberId_Status",
                schema: "dbo",
                table: "Enrollments",
                columns: new[] { "MemberId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_PlanId",
                schema: "dbo",
                table: "Enrollments",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_LastName_FirstName",
                schema: "dbo",
                table: "Members",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Members_Mbi",
                schema: "dbo",
                table: "Members",
                column: "Mbi",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Pending",
                schema: "dbo",
                table: "Outbox",
                columns: new[] { "Type", "AvailableAtUtc" },
                filter: "[CompletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Plans_PlanCode_ContractYear",
                schema: "dbo",
                table: "Plans",
                columns: new[] { "PlanCode", "ContractYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureFees_ProcedureCode_EffectiveYear",
                schema: "dbo",
                table: "ProcedureFees",
                columns: new[] { "ProcedureCode", "EffectiveYear" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BenefitAccumulators",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ClaimLines",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Enrollments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ProcedureFees",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Claims",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Members",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Plans",
                schema: "dbo");
        }
    }
}
