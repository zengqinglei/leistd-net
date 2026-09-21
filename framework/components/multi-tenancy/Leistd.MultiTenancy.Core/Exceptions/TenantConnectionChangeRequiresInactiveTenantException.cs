using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示尝试在启用中的租户上做一次会改变数据落点的连接变更。
/// </summary>
/// <remarks>
/// <para>两类写入落在这一档：<b>改或删已有的那一行</b>（路由从一个库指向另一个库），
/// 以及<b>给一条登记都没有的租户登记第一条</b>（把它从"不分库"变成"分库"）。
/// 前者让热实例与冷实例同时写入不同物理库；后者把它已有的数据连同租户管理员一起留在旧库里，
/// 新库是空的，租户当场登不上。</para>
/// <para><b>已是分库租户、补一个此前没有的名字不在此列</b>：那个服务此前就是失败关闭的，
/// 没有数据可搁浅，补登是修复动作，启用态下照常放行。</para>
/// </remarks>
public class TenantConnectionChangeRequiresInactiveTenantException : ConflictException
{
    /// <summary>构造异常。</summary>
    /// <param name="tenantId">目标租户</param>
    public TenantConnectionChangeRequiresInactiveTenantException(Guid tenantId)
        : base(
            $"Tenant '{tenantId}' must be deactivated before a connection change that moves where its data lives. " +
            "Changing an existing route while the tenant is serving lets cached and cold instances write to " +
            "different physical databases at the same time; registering its very first connection strands the " +
            "data it already has, its administrator included, in the database it has been using until now. " +
            "Deactivate the tenant, wait for both the access token lifetime and the route cache TTL to elapse, " +
            "migrate the data, then change the route.")
    {
        TenantId = tenantId;
        WithCode(MultiTenancyErrorCodes.ConnectionChangeRequiresInactiveTenant);
    }

    /// <summary>获取目标租户标识。</summary>
    public Guid TenantId { get; }
}
