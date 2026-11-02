#if (IncludeWebhooks)
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookEvent",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscription",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EventTypes = table.Column<List<string>>(type: "text[]", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: false),
                    Secret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PreviousSecret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PreviousSecretExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DisabledReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DisabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstFailureAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscription", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDelivery",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NextTrigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AutoAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDelivery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDelivery_WebhookEvent_EventId",
                        column: x => x.EventId,
                        principalSchema: "companyname-projectname",
                        principalTable: "WebhookEvent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WebhookDelivery_WebhookSubscription_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalSchema: "companyname-projectname",
                        principalTable: "WebhookSubscription",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookAttempt",
                schema: "companyname-projectname",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestHeaders = table.Column<string>(type: "text", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Response = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookAttempt_WebhookDelivery_DeliveryId",
                        column: x => x.DeliveryId,
                        principalSchema: "companyname-projectname",
                        principalTable: "WebhookDelivery",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookAttempt_DeliveryId",
                schema: "companyname-projectname",
                table: "WebhookAttempt",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDelivery_CreationTime",
                schema: "companyname-projectname",
                table: "WebhookDelivery",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDelivery_EventId",
                schema: "companyname-projectname",
                table: "WebhookDelivery",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDelivery_Status_NextAttemptAt",
                schema: "companyname-projectname",
                table: "WebhookDelivery",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDelivery_SubscriptionId_CreationTime",
                schema: "companyname-projectname",
                table: "WebhookDelivery",
                columns: new[] { "SubscriptionId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvent_OccurredAt",
                schema: "companyname-projectname",
                table: "WebhookEvent",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscription_TenantId_IsEnabled",
                schema: "companyname-projectname",
                table: "WebhookSubscription",
                columns: new[] { "TenantId", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookAttempt",
                schema: "companyname-projectname");

            migrationBuilder.DropTable(
                name: "WebhookDelivery",
                schema: "companyname-projectname");

            migrationBuilder.DropTable(
                name: "WebhookEvent",
                schema: "companyname-projectname");

            migrationBuilder.DropTable(
                name: "WebhookSubscription",
                schema: "companyname-projectname");
        }
    }
}
#endif
