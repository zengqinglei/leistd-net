#if (OpenIddictServer || RemoteTokenAuth)
using CompanyName.ProjectName.Application.Systems.AppServices;
using CompanyName.ProjectName.Application.Systems.Dtos;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.Systems.Constants;
#endif
#if (ResourceBrowserSession)
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using CompanyName.ProjectName.Application.Shared;
using Microsoft.Extensions.Options;
#endif
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>系统入口对登录用户开放；Identity 同时允许持有目录 scope 的机器主体。</summary>
[Route("api/v1/systems")]
#if (OpenIddictServer)
[Authorize(Policy = SystemDirectoryPolicies.Read)]
#else
[Authorize]
#endif
public sealed class SystemController(ISystemAppService systems
#if (ResourceBrowserSession)
    , IOptionsMonitor<OpenIdConnectOptions> identity
#endif
    ) : BaseController
{
    [HttpGet]
    public async Task<IReadOnlyList<SystemOutputDto>> GetListAsync(CancellationToken cancellationToken)
    {
        var result = await systems.GetListAsync(cancellationToken);
#if (ResourceBrowserSession)
        return result.Select(system => system with { IsCurrent = system.Key == identity.Get(AuthenticationSchemeNames.OpenIdConnect).ClientId }).ToArray();
#elif (OpenIddictServer)
        return result.Select(system => system.IsCurrent && string.IsNullOrEmpty(system.HomeUrl)
            ? system with { HomeUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/" } : system).ToArray();
#else
        return result;
#endif
    }
}
#endif
