using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations.Identity
{
    public partial class AddUserSessionPolicy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "KeepSignedIn",
                schema: "companyname-projectname",
                table: "UserSessions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AuthenticatedAt",
                schema: "companyname-projectname",
                table: "UserSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "companyname-projectname"."UserSessions"
                SET "AuthenticatedAt" = "CreationTime", "KeepSignedIn" = TRUE
                """);

            migrationBuilder.AlterColumn<bool>(
                name: "KeepSignedIn",
                schema: "companyname-projectname",
                table: "UserSessions",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "AuthenticatedAt",
                schema: "companyname-projectname",
                table: "UserSessions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeepSignedIn",
                schema: "companyname-projectname",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "AuthenticatedAt",
                schema: "companyname-projectname",
                table: "UserSessions");
        }
    }
}
