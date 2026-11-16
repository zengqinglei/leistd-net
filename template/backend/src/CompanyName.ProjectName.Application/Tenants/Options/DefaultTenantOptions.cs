#if (LocalIdentity && IncludeMultiTenancy)
namespace CompanyName.ProjectName.Application.Tenants.Options;

/// <summary>空库首次启动时的默认租户；不配置名称即跳过。</summary>
public sealed class DefaultTenantOptions
{
    public const string SectionName = "MultiTenancy:DefaultTenant";
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
}
#endif
