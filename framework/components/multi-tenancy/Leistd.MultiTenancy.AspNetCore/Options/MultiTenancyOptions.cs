
namespace Leistd.MultiTenancy.AspNetCore.Options;

/// <summary>
/// 配置 Web 宿主的多租户解析与校验。
/// </summary>
public class MultiTenancyOptions
{
    /// <summary>获取配置节名称 <c>Leistd:MultiTenancy</c>。</summary>
    public const string SectionName = "Leistd:MultiTenancy";

    /// <summary>默认的租户线索请求头名称 <c>X-Tenant</c>。</summary>
    public const string DefaultHeaderName = "X-Tenant";

    /// <summary>
    /// 获取或设置承载租户线索的请求头名称。默认 <see cref="DefaultHeaderName"/>。
    /// </summary>
    /// <remarks>
    /// 匿名请求的提示通道：值可以是租户 Id 或名称，是否采信由校验决定。
    /// 与服务间委托的 <c>X-Tenant-Id</c>（只带 GUID、须受信调用方）刻意不同名。
    /// </remarks>
    public string HeaderName { get; set; } = DefaultHeaderName;

    /// <summary>
    /// 获取或设置承载租户线索的查询参数名称。
    /// </summary>
    public string QueryStringParameterName { get; set; } = "tenant";

    /// <summary>
    /// 获取或设置是否校验解析出的租户存在且启用。
    /// </summary>
    /// <remarks>
    /// <b>只有不持有租户注册表的资源服务才应置为 <see langword="false"/></b>——它的租户上下文只来自已验证令牌的 claim。
    /// 置 <see langword="false"/> 时解析链被强制收窄到只有 <c>CurrentPrincipalTenantResolveContributor</c>，宿主对链的增删排序一律无效。
    /// 置 <see langword="true"/> 却未注册 <c>ITenantStore</c> 时启动期抛 <c>OptionsValidationException</c>。
    /// </remarks>
    public bool ValidateResolvedTenant { get; set; } = true;

    /// <summary>
    /// 获取或设置子域名解析格式；为空时禁用子域名解析。
    /// </summary>
    /// <remarks>
    /// 写错在启动期抛 <c>OptionsValidationException</c>，不会静默退回请求头解析。
    /// 恰好一个 <c>{0}</c>，占位符所在段之后必须还有固定的基础域（<c>example.{0}</c> 非法）；纯 ASCII、不写端口。
    /// 完整格式契约与部署要点见 multi-tenancy 组件文档。
    /// </remarks>
    public string? DomainFormat { get; set; }
}
