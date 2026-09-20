using Leistd.MultiTenancy.Stores;

namespace Leistd.MultiTenancy.Provisioning;

/// <summary>
/// 启用租户的前置条件，例如"租户里至少有一个用户"。由宿主实现，可注册多个。
/// </summary>
/// <remarks>
/// <para>只在经管理用例<b>手动启用</b>时依次调用，创建流程末尾的自动启用不经过它（开通刚写完初始数据）。
/// 不满足时抛带码的业务异常，启用随之中止。</para>
/// <para>调用时处于宿主上下文；要查租户自己的数据，实现自己切到租户上下文并新开工作单元——
/// 只切上下文不开新工作单元，取到的仍是请求里早已绑定到宿主库的 DbContext。</para>
/// </remarks>
public interface ITenantActivationGuard
{
    /// <summary>确认租户可以启用；不满足时抛出。</summary>
    /// <param name="tenant">待启用的租户。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task EnsureCanActivateAsync(TenantConfiguration tenant, CancellationToken cancellationToken = default);
}
