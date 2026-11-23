using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddSettingAuditAndChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                schema: "companyname-projectname",
                table: "SettingRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastModifierId",
                schema: "companyname-projectname",
                table: "SettingRecord",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastModifierName",
                schema: "companyname-projectname",
                table: "SettingRecord",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SettingCheckRecord",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeKey = table.Column<string>(type: "character varying(192)", maxLength: 192, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CheckTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CheckerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false),
                    RunSequence = table.Column<long>(type: "bigint", nullable: false),
                    ResultSequence = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingCheckRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettingCheckRecord_ScopeKey_Name",
                schema: "companyname-projectname",
                table: "SettingCheckRecord",
                columns: new[] { "ScopeKey", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SettingCheckRecord",
                schema: "companyname-projectname");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                schema: "companyname-projectname",
                table: "SettingRecord");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                schema: "companyname-projectname",
                table: "SettingRecord");

            migrationBuilder.DropColumn(
                name: "LastModifierName",
                schema: "companyname-projectname",
                table: "SettingRecord");
        }
    }
}
