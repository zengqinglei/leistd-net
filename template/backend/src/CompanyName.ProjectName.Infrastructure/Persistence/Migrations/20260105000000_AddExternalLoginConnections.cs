#if (IncludeExternalLogin)
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalLoginConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalLoginConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderUsername = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderAvatarUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AccessToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RefreshToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifierId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeleterId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalLoginConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalLoginConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLoginConnections_Provider_ProviderUserId",
                table: "ExternalLoginConnections",
                columns: new[] { "Provider", "ProviderUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLoginConnections_UserId",
                table: "ExternalLoginConnections",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalLoginConnections");
        }
    }
}
#endif
