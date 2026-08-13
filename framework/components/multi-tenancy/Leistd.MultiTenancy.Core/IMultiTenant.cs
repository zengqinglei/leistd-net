namespace Leistd.MultiTenancy;

/// <summary>
/// 多租户实体标记接口
/// </summary>
/// <remarks>
/// <para><see cref="TenantId"/> 为 <c>null</c> 表示宿主（Host）数据——同一实体类型同时服务宿主与租户两侧。</para>
/// <para>实现该接口的实体会被 DDD 基座的全局查询过滤器按当前租户自动过滤，
/// 并在写入时由 <c>MultiTenantSaveChangesInterceptor</c> 自动填充当前租户 Id。</para>
/// <para>宿主侧专属实体（如租户注册表本身）不应实现该接口。</para>
/// </remarks>
public interface IMultiTenant
{
    /// <summary>
    /// 所属租户 Id，<c>null</c> 表示宿主数据
    /// </summary>
    Guid? TenantId { get; }
}
