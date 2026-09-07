using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        var implementationType = typeof(EfCoreSettingStore<TDbContext>);

        // 两个上下文各注册一次时 Microsoft DI 静默取最后一条：设置会落进宿主没预期的那个库，
        // 读回来的也是那一个，全程没有信号。设置只有一个权威存储，这里直接拒绝。
        //
        // 必须枚举全部而不是看第一条：单服务解析由最后一条胜出，只看第一条会在
        // "第一条恰是本类型、后面还有别的实现"时放行，而实际胜出的仍是后者。
        // keyed 注册要排除：它按键解析，不参与 ISettingStore 的单服务解析，不构成冲突。
        var existing = services
            .Where(d => d.ServiceType == typeof(ISettingStore) && !d.IsKeyedService)
            .ToList();

        var conflicting = existing.FirstOrDefault(d => d.ImplementationType != implementationType);
        if (conflicting is not null)
        {
            throw new InvalidOperationException(
                $"An {nameof(ISettingStore)} is already registered as " +
                $"'{conflicting.ImplementationType?.FullName ?? "<factory>"}'. Settings have a single authoritative " +
                $"store; registering '{implementationType.FullName}' would silently win by ordering and values " +
                "would be written to a different database than the caller expects. Map SettingRecord in one DbContext.");
        }

        services.AddSettingsCore();
        // 同一个上下文重复登记按幂等处理；TryAdd 在这里已足够，因为上面已排除异类实现。
        if (existing.Count == 0)
        {
            services.AddTransient<ISettingStore, EfCoreSettingStore<TDbContext>>();
        }

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
