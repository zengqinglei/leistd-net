#if (TenantWorkspace)
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations.Control
{
    /// <inheritdoc />
    public partial class AddWorkspaceAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserTenantAccesses",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTenantAccesses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTenantAccesses_HomeUserId_TenantId",
                schema: "companyname-projectname",
                table: "UserTenantAccesses",
                columns: new[] { "HomeUserId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserTenantAccesses",
                schema: "companyname-projectname");
        }
    }
}
#endif
