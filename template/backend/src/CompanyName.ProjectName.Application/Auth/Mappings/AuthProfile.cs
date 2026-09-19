#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Auth.Entities;
using Leistd.ObjectMapping.Mapster;
using Leistd.ObjectMapping.Mapster.Mapping;
using Mapster;

namespace CompanyName.ProjectName.Application.Auth.Mappings;

/// <summary>
/// 认证模块映射配置
/// </summary>
public class AuthProfile : MapsterProfile
{
    /// <summary>MapContext 参数名：发起请求的这台设备的会话 Id，用来标出"当前设备"。</summary>
    public const string CurrentSessionIdKey = "CurrentSessionId";

    protected override void ConfigureMappings()
    {
        CreateMap<UserSession, UserSessionOutputDto>()
            .Map(dest => dest.IsCurrent, src => IsCurrentSession(src.Id));
#if (ExternalLogin)

        CreateMap<ExternalLoginConnection, ExternalLoginLinkOutputDto>();
#endif
    }

    private static bool IsCurrentSession(Guid sessionId) =>
        MapContext.Current?.Parameters.TryGetValue(CurrentSessionIdKey, out var value) == true &&
        value is Guid current &&
        current == sessionId;
}
#endif
