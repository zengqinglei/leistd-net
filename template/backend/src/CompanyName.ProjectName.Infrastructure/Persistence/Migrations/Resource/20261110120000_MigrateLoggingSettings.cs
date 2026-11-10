using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyName.ProjectName.Infrastructure.Persistence.Migrations.Resource
{
    /// <summary>日志设置换用 Microsoft.Extensions.Logging 的取值域：最小级别改用 LogLevel 名称，请求日志级别改为开关。</summary>
    /// <remarks>
    /// <para>只改宿主级行（<c>ScopeKey = 'host:t'</c>，租户与用户都为空）：同名的宿主用户偏好与租户行不是这两项设置的值。
    /// 旧键不存在时不补行，取值域之外的行不动；是否已执行由迁移历史记录。</para>
    /// <para>请求日志按默认 Information 基线映射：原来记成 Verbose、Debug 的正常请求不会输出，映射为关闭，其余为开启。
    /// 降级时开关映射回 Verbose（关）或 Information（开），不恢复原来的档位。</para>
    /// </remarks>
    public partial class MigrateLoggingSettings : Migration
    {
        private const string HostRow =
            "\"ScopeKey\" = 'host:t' AND \"TenantId\" IS NULL AND \"UserId\" IS NULL";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE "companyname-projectname"."SettingRecord"
                SET "Value" = CASE "Value" WHEN 'Verbose' THEN 'Trace' WHEN 'Fatal' THEN 'Critical' END
                WHERE "Name" = 'Logging.MinimumLevel' AND {HostRow} AND "Value" IN ('Verbose', 'Fatal');
                """);
            migrationBuilder.Sql($"""
                UPDATE "companyname-projectname"."SettingRecord"
                SET "Name" = 'Logging.RequestLogging',
                    "Value" = CASE WHEN "Value" IN ('Verbose', 'Debug') THEN 'false' ELSE 'true' END
                WHERE "Name" = 'Logging.RequestLevel' AND {HostRow}
                    AND "Value" IN ('Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE "companyname-projectname"."SettingRecord"
                SET "Value" = CASE "Value" WHEN 'Trace' THEN 'Verbose' WHEN 'Critical' THEN 'Fatal' END
                WHERE "Name" = 'Logging.MinimumLevel' AND {HostRow} AND "Value" IN ('Trace', 'Critical');
                """);
            migrationBuilder.Sql($"""
                UPDATE "companyname-projectname"."SettingRecord"
                SET "Name" = 'Logging.RequestLevel',
                    "Value" = CASE "Value" WHEN 'false' THEN 'Verbose' ELSE 'Information' END
                WHERE "Name" = 'Logging.RequestLogging' AND {HostRow} AND "Value" IN ('true', 'false');
                """);
        }
    }
}
