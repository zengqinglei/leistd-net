using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Leistd.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Settings.Abstractions;
using Leistd.Settings.EntityFrameworkCore.EntityConfigurations;
using Leistd.Settings.EntityFrameworkCore.Entities;
using Leistd.Settings.EntityFrameworkCore.Stores;

namespace Leistd.Settings.EntityFrameworkCore;

/// <summary>
/// 设置 EF Core 持久化依赖注入与模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 EF Core 设置存储（基于指定 DbContext）。
    /// </summary>
    /// <remarks>
    /// <b>前置</b>：宿主须已注册 <c>AddUnitOfWork()</c> 与 <c>AddUnitOfWorkEfCore()</c>——
    /// 本存储经 <c>IDbContextProvider&lt;TDbContext&gt;</c> 取上下文
    /// （只有它会设置 <c>DbContextCreationContext.Current</c>，从而拿到本工作单元已解析的连接）。
    /// 与 <c>AddAuthorizationEfCore</c> 同一约定：组件不替其它组件注册基础设施。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddSettingsEfCore&lt;AppDbContext&gt;();
    ///
    /// // DbContext 里映射设置表
    /// protected override void ConfigureModel(ModelBuilder modelBuilder)
    ///     =&gt; modelBuilder.ConfigureSettings();
    /// </code>
    /// </example>
    /// <typeparam name="TDbContext">承载设置表的 DbContext。</typeparam>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddSettingsEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {

        // 设置只有一个权威存储：两个上下文各注册一次时会静默取一条，值写进宿主没预期的库。
        services.EnsureSingleAuthoritative<ISettingStore, EfCoreSettingStore<TDbContext>>(
            ServiceLifetime.Transient,
            "Settings have a single authoritative store; map SettingRecord in one DbContext.");

        services.AddSettingsCore();
        services.TryAddTransient<ISettingStore, EfCoreSettingStore<TDbContext>>();

        return services;
    }

    /// <summary>
    /// 将 <see cref="SettingRecord"/> 实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    /// <param name="modelBuilder">模型构建器。</param>
    public static ModelBuilder ConfigureSettings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SettingRecordConfiguration());
        return modelBuilder;
    }
}
