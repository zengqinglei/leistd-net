using System.Data.Common;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.Provisioning;

/// <summary>
/// 把开通时的数据库错误翻译成调用方能照着改的 400。
/// </summary>
/// <remarks>
/// <para>开通失败最常见的原因是那个专属库用不了：连不上、库不存在、建好了没迁移。三种都是调用方能改对的，
/// 却因为异常出自数据库驱动而兜底成 500。默认实现按 SQLSTATE 分流（码值取 PostgreSQL），换数据库时替换它。</para>
/// <para>翻译结果一律不回显连接串、主机与端口：错误响应会同时进入响应体、前端提示与服务端日志。</para>
/// </remarks>
public interface ITenantDatabaseErrorDescriber
{
    /// <summary>翻译开通失败；不是数据库原因时返回 <see langword="null"/>，原异常照常抛出。</summary>
    /// <param name="error">开通或启用时抛出的异常。</param>
    BusinessException? Describe(Exception error);
}

// 逐层往内找 DbException：驱动异常常被 EF 或工作单元包上几层
internal sealed class SqlStateTenantDatabaseErrorDescriber : ITenantDatabaseErrorDescriber
{
    public BusinessException? Describe(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (current is not DbException database)
            {
                continue;
            }

            // 连接阶段失败（主机不可达、端口拒绝）不是服务端返回的错误，没有 SQLSTATE
            if (string.IsNullOrEmpty(database.SqlState))
            {
                return Describe(
                    "The tenant's dedicated database is unreachable. Check that the host and port are reachable and the database server is running.",
                    MultiTenancyErrorCodes.DedicatedDatabaseUnreachable);
            }

            return database.SqlState switch
            {
                // invalid_catalog_name：连上了服务器，但那个库不存在
                "3D000" => Describe(
                    "The database named in the connection string does not exist. Create and migrate it first, then create the tenant.",
                    MultiTenancyErrorCodes.DedicatedDatabaseMissing),

                // undefined_table：库在，但没迁移过
                "42P01" => Describe(
                    "The database exists but has not been migrated; its tables are missing. Migrate it first.",
                    MultiTenancyErrorCodes.DedicatedDatabaseNotMigrated),

                // invalid_authorization_specification / invalid_password
                "28000" or "28P01" => Describe(
                    "The database rejected the credentials in the connection string.",
                    MultiTenancyErrorCodes.DedicatedDatabaseRejected),

                _ => Describe(
                    $"The tenant's dedicated database reported error {database.SqlState}.",
                    MultiTenancyErrorCodes.DedicatedDatabaseFailed)
                    .WithData("SqlState", database.SqlState)
            };
        }

        return null;
    }

    private static BusinessException Describe(string message, string code)
        => new BadRequestException(message).WithCode(code);
}
