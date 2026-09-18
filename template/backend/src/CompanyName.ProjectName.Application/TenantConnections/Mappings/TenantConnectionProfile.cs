#if (LocalIdentity)
using CompanyName.ProjectName.Application.TenantConnections.Dtos;
using CompanyName.ProjectName.Domain.Tenants.Connections;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.ObjectMapping.Mapster;
using Leistd.ObjectMapping.Mapster.Mapping;
using Mapster;

namespace CompanyName.ProjectName.Application.TenantConnections.Mappings;

/// <summary>
/// 租户连接映射配置
/// </summary>
/// <remarks>
/// 管理面只回名字与版本、内部下发才带明文连接串：这条边界由各输出 DTO 的字段集表达，映射本身都是同名投影。
/// </remarks>
public class TenantConnectionProfile : MapsterProfile
{
    /// <summary>MapContext 参数名：连接所属的租户（目录条目本身不带租户）。</summary>
    public const string TenantIdKey = "TenantId";

    protected override void ConfigureMappings()
    {
        CreateMap<TenantConnectionEntry, TenantConnectionOutputDto>()
            .Map(dest => dest.TenantId, src => ResolveTenantId());
        CreateMap<TenantConnectionConfiguration, TenantConnectionOutputDto>();
        CreateMap<TenantConnectionConfiguration, TenantConnectionDetailOutputDto>();
        CreateMap<TenantConnectionLookupResult, TenantRuntimeConnectionOutputDto>();
        CreateMap<TenantMigrationConnection, TenantMigrationConnectionOutputDto>();
    }

    // 目录条目只在"某个租户的连接列表"里出现，调用方必须给出租户；漏给是调用错误，不是一个空租户
    private static Guid ResolveTenantId() =>
        MapContext.Current?.Parameters.TryGetValue(TenantIdKey, out var value) == true && value is Guid tenantId
            ? tenantId
            : throw new InvalidOperationException($"Mapping a tenant connection entry requires the '{TenantIdKey}' context parameter.");
}
#endif
