#if (IncludeNotifications)
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeistdNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Link = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RelatedEntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RelatedEntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeistdNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeistdNotifications_UserId_CreationTime",
                table: "LeistdNotifications",
                columns: new[] { "UserId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_LeistdNotifications_UserId_IsRead",
                table: "LeistdNotifications",
                columns: new[] { "UserId", "IsRead" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeistdNotifications");
        }
    }
}
#endif
