#if (LocalIdentity)
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.MultiTenancy.Stores;
using Leistd.ObjectMapping.Mapster;
using Leistd.ObjectMapping.Mapster.Mapping;

namespace CompanyName.ProjectName.Application.Tenants.Mappings;

/// <summary>
/// 租户映射配置
/// </summary>
/// <remarks>
/// 两个输出 DTO 都是 <see cref="TenantConfiguration"/> 的同名字段投影，差别只在
/// <c>TenantLookupOutputDto</c> 少一个 <c>CreationTime</c>——匿名探测只回选择租户所需的最小信息。
/// 投影差异由 DTO 的字段集表达，不在应用服务里手工 new。
/// </remarks>
public class TenantProfile : MapsterProfile
{
    protected override void ConfigureMappings()
    {
        CreateMap<TenantConfiguration, TenantOutputDto>();
        CreateMap<TenantConfiguration, TenantLookupOutputDto>();
    }
}
#endif
