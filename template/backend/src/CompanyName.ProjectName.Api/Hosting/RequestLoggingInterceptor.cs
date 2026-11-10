using CompanyName.ProjectName.Api.Options;
using Leistd.MultiTenancy.Context;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>请求完成日志的开关与租户字段。</summary>
/// <remarks>
/// <para>关闭时只跳过状态码低于 500 的请求：失败请求是排障必需的，不随运维开关关掉。
/// 内置 HTTP 日志在末尾按参数是否为空决定是否输出，所以跳过时清空已收集的请求字段。</para>
/// <para>租户取管道写入 <see cref="HttpContext.Items"/> 的值（见 <see cref="CaptureTenant"/>）：
/// 完成日志在租户中间件退出之后才写，那时 <see cref="ICurrentTenant"/> 已恢复为外层值。</para>
/// </remarks>
internal sealed class RequestLoggingInterceptor(IOptionsMonitor<RequestLoggingOptions> options) : IHttpLoggingInterceptor
{
    private static readonly object TenantIdItemKey = new();

    /// <summary>记下本请求解析出的租户；放在租户解析之后的管道里，宿主请求记为 <see langword="null"/>。</summary>
    public static void CaptureTenant(HttpContext context, Guid? tenantId) => context.Items[TenantIdItemKey] = tenantId;

    /// <inheritdoc />
    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext)
    {
        if (!options.CurrentValue.Enabled && logContext.HttpContext.Response.StatusCode < StatusCodes.Status500InternalServerError)
        {
            logContext.LoggingFields = HttpLoggingFields.None;
            logContext.Parameters.Clear();
            return ValueTask.CompletedTask;
        }

        logContext.HttpContext.Items.TryGetValue(TenantIdItemKey, out var tenantId);
        logContext.AddParameter(TenantLogKeys.TenantId, tenantId as Guid?);
        return ValueTask.CompletedTask;
    }
}
