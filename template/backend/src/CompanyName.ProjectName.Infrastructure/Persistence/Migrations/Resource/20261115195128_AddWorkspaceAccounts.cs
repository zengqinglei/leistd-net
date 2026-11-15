#if (TenantWorkspace)
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations.Resource
{
    /// <inheritdoc />
    public partial class AddWorkspaceAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Username",
                schema: "companyname-projectname",
                table: "Users",
                type: "character varying(73)",
                maxLength: 73,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<Guid>(
                name: "HomeUserId",
                schema: "companyname-projectname",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_HomeUserId",
                schema: "companyname-projectname",
                table: "Users",
                columns: new[] { "TenantId", "HomeUserId" },
                unique: true,
                filter: "\"HomeUserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_HomeUserId",
                schema: "companyname-projectname",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HomeUserId",
                schema: "companyname-projectname",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                schema: "companyname-projectname",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(73)",
                oldMaxLength: 73);
        }
    }
}
#endif
