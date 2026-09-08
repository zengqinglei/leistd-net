using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Auditing.Abstractions;

namespace Leistd.Auditing.EntityFrameworkCore.Extensions;

/// <summary>
/// 提供实体进入 <see cref="EntityState.Added"/> 时的变更跟踪扩展。
/// </summary>
/// <remarks>
/// <para>创建审计与租户归属必须在进入跟踪时落定，避免延迟提交跨越当前租户或主体作用域。</para>
/// <para><see cref="OnEntityEnteringAdded"/> 本身与审计无关，落在本包是因为它的两个使用方
/// （DDD 基座的 <c>BaseDbContext</c> 与宿主控制面上下文）都已依赖审计；
/// 移到工作单元包会让本包反过来依赖工作单元，移到 ddd-struct 则违反 components 的依赖方向。</para>
/// </remarks>
public static class EntityTrackingExtensions
{
    /// <summary>
    /// 在实体进入 <see cref="EntityState.Added"/> 时调用处理程序。
    /// </summary>
    /// <param name="changeTracker">目标上下文的变更跟踪器</param>
    /// <param name="handler">
    /// 落值动作。同一个实体可能被调用多次（先 <c>Added</c> 后又被改状态再改回来），
    /// 因此 <paramref name="handler"/> 必须幂等——只在目标值仍为空时写入
    /// </param>
    /// <remarks>同时订阅 <see cref="ChangeTracker.Tracked"/> 和 <see cref="ChangeTracker.StateChanged"/>，覆盖首次跟踪与后续状态切换。</remarks>
    public static void OnEntityEnteringAdded(this ChangeTracker changeTracker, Action<EntityEntry> handler)
    {
        ArgumentNullException.ThrowIfNull(changeTracker);
        ArgumentNullException.ThrowIfNull(handler);

        changeTracker.Tracked += (_, e) =>
        {
            // 查询物化的实体不得触发创建审计。
            if (e.FromQuery)
            {
                return;
            }

            Invoke(e.Entry);
        };

        changeTracker.StateChanged += (_, e) =>
        {
            if (e.NewState != EntityState.Added)
            {
                return;
            }

            Invoke(e.Entry);
        };

        void Invoke(EntityEntry entry)
        {
            if (entry.State == EntityState.Added)
            {
                handler(entry);
            }
        }
    }

    /// <summary>
    /// 为宿主控制面的普通 <see cref="DbContext"/> 启用创建审计。
    /// </summary>
    /// <param name="changeTracker">目标上下文的变更跟踪器</param>
    /// <param name="serviceProvider">
    /// 用于解析 <see cref="IAuditPropertySetter"/>。为 <see langword="null"/> 时不订阅——
    /// 设计时工具与直接 <c>new</c> 出上下文的批处理没有容器，也没有"谁创建的"这个概念
    /// </param>
    /// <remarks>仅供不继承 <c>BaseDbContext</c> 的宿主控制面上下文使用；审计服务延迟到首次写入时解析。</remarks>
    public static void EnableCreationAuditing(
        this ChangeTracker changeTracker,
        IServiceProvider? serviceProvider)
    {
        if (serviceProvider is null)
        {
            return;
        }

        IAuditPropertySetter? setter = null;

        changeTracker.OnEntityEnteringAdded(entry =>
        {
            // 显式启用审计后缺少依赖必须立即失败，不能静默丢失审计。
            setter ??= serviceProvider.GetRequiredService<IAuditPropertySetter>();
            setter.SetCreationProperties(entry);
        });
    }
}
