#if (IncludeDistributedEvents)
using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations.Resource
{
    /// <inheritdoc />
    public partial class AddDistributedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DistributedEventInbox",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OrderingKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Coordinates = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false),
                    ReplayCount = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistributedEventInbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DistributedEventOutbox",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OrderingKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstFailureAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RedeliverCount = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistributedEventOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DistributedEventRejection",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Transport = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EventName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EventId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawBody = table.Column<byte[]>(type: "bytea", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: false),
                    Coordinates = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RedecodeCount = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistributedEventRejection", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventInbox_EventId_Consumer",
                schema: "companyname-projectname",
                table: "DistributedEventInbox",
                columns: new[] { "EventId", "Consumer" },
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventInbox_EventName_Status",
                schema: "companyname-projectname",
                table: "DistributedEventInbox",
                columns: new[] { "EventName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventInbox_Status_LastModificationTime",
                schema: "companyname-projectname",
                table: "DistributedEventInbox",
                columns: new[] { "Status", "LastModificationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventInbox_Status_NextAttemptAt_Id",
                schema: "companyname-projectname",
                table: "DistributedEventInbox",
                columns: new[] { "Status", "NextAttemptAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventInbox_TenantId_EventId_Consumer",
                schema: "companyname-projectname",
                table: "DistributedEventInbox",
                columns: new[] { "TenantId", "EventId", "Consumer" },
                unique: true,
                filter: "\"TenantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventInbox_TenantId_EventName_OrderingKey_Id",
                schema: "companyname-projectname",
                table: "DistributedEventInbox",
                columns: new[] { "TenantId", "EventName", "OrderingKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventOutbox_EventId",
                schema: "companyname-projectname",
                table: "DistributedEventOutbox",
                column: "EventId",
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventOutbox_EventName_Status",
                schema: "companyname-projectname",
                table: "DistributedEventOutbox",
                columns: new[] { "EventName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventOutbox_Status_LastModificationTime",
                schema: "companyname-projectname",
                table: "DistributedEventOutbox",
                columns: new[] { "Status", "LastModificationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventOutbox_Status_NextAttemptAt_Id",
                schema: "companyname-projectname",
                table: "DistributedEventOutbox",
                columns: new[] { "Status", "NextAttemptAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventOutbox_TenantId_EventId",
                schema: "companyname-projectname",
                table: "DistributedEventOutbox",
                columns: new[] { "TenantId", "EventId" },
                unique: true,
                filter: "\"TenantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventOutbox_TenantId_EventName_OrderingKey_Id",
                schema: "companyname-projectname",
                table: "DistributedEventOutbox",
                columns: new[] { "TenantId", "EventName", "OrderingKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedEventRejection_Status_LastModificationTime",
                schema: "companyname-projectname",
                table: "DistributedEventRejection",
                columns: new[] { "Status", "LastModificationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DistributedEventInbox",
                schema: "companyname-projectname");

            migrationBuilder.DropTable(
                name: "DistributedEventOutbox",
                schema: "companyname-projectname");

            migrationBuilder.DropTable(
                name: "DistributedEventRejection",
                schema: "companyname-projectname");
        }
    }
}
#endif
