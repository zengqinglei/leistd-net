#if (LocalIdentity)
using CompanyName.ProjectName.Application.TenantConnections.Dtos;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.ObjectMapping.Mapster;
using Leistd.ObjectMapping.Mapster.Mapping;

namespace CompanyName.ProjectName.Application.TenantConnections.Mappings;

/// <summary>
/// 租户连接配置映射配置
/// </summary>
/// <remarks>
/// 三个输出 DTO 都是 <see cref="TenantConnectionConfiguration"/> 的同名字段投影，差别只在
/// 各自暴露哪个 Secret 引用——运行面只回 <c>RuntimeSecretReference</c>，迁移面只回
/// <c>MigrationSecretReference</c>。<b>这个差异是安全边界</b>，由 DTO 的字段集表达：
/// 少写一个字段就等于少暴露一个引用，不必在应用服务里逐个手工挑字段。
/// </remarks>
public class TenantConnectionProfile : MapsterProfile
{
    protected override void ConfigureMappings()
    {
        CreateMap<TenantConnectionConfiguration, TenantConnectionOutputDto>();
        CreateMap<TenantConnectionConfiguration, TenantRuntimeConnectionOutputDto>();
        CreateMap<TenantConnectionConfiguration, TenantMigrationConnectionOutputDto>();
    }
}
#endif
