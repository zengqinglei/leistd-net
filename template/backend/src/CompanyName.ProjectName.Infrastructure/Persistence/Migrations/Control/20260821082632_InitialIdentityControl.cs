using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations.Control
{
    /// <inheritdoc />
    public partial class InitialIdentityControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "companyname-projectname");

            migrationBuilder.CreateTable(
                name: "TenantRecord",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeleterId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantConnectionRecord",
                schema: "companyname-projectname",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DatabaseMode = table.Column<int>(type: "integer", nullable: false),
                    RuntimeSecretReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MigrationSecretReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantConnectionRecord", x => x.TenantId);
                    table.CheckConstraint("CK_TenantConnectionRecord_ModeSecrets", "(\"DatabaseMode\" = 0 AND \"RuntimeSecretReference\" IS NULL AND \"MigrationSecretReference\" IS NULL) OR (\"DatabaseMode\" = 1 AND \"RuntimeSecretReference\" IS NOT NULL AND \"MigrationSecretReference\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TenantConnectionRecord_TenantRecord_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "companyname-projectname",
                        principalTable: "TenantRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantRecord_NormalizedName",
                schema: "companyname-projectname",
                table: "TenantRecord",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantConnectionRecord",
                schema: "companyname-projectname");

            migrationBuilder.DropTable(
                name: "TenantRecord",
                schema: "companyname-projectname");
        }
    }
}
