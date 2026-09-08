using Leistd.Auditing.EntityFrameworkCore.Interceptors;
using Leistd.Ddd.Infrastructure.EventBus;
using Leistd.Ddd.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Ddd.Infrastructure.Persistence.Extensions;

/// <summary>
/// 提供 DDD 基座的 DbContext 拦截器配置扩展。
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// 为业务 <see cref="DbContext"/> 添加 DDD 保存拦截器。
    /// </summary>
    /// <remarks>
    /// 挂载修改/删除审计（含软删除转换）、领域事件收集与发布、并发标记换发三项。
    /// <b>控制面上下文不要用本方法</b>：它通常只需要审计，单独挂 <c>AuditSaveChangesInterceptor</c> 即可。
    /// </remarks>
    /// <param name="optionsBuilder">DbContext 选项构建器</param>
    /// <param name="serviceProvider">
    /// 用于解析拦截器的服务提供器，即 <c>AddDbContext&lt;T&gt;((sp, options) =&gt; ...)</c> 回调里的 <c>sp</c>
    /// </param>
    public static DbContextOptionsBuilder AddDddInterceptors(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return optionsBuilder.AddInterceptors(
            serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<LocalEventSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<ConcurrencyStampSaveChangesInterceptor>());
    }
}
