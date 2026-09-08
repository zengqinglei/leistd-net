using Leistd.Security.Clients;
using Leistd.Security.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 服务信息与调用诊断端点：供其他服务经本服务的 Client 包
/// （CompanyName.ProjectName.Client）探活与联调。
/// </summary>
[Route("api/v1/service-info")]
public sealed class ServiceInfoController : BaseController
{
    /// <summary>
    /// 服务基础信息（匿名）：服务名、版本与服务器时间。
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public ServiceInfoOutputDto Get()
    {
        var assemblyName = typeof(ServiceInfoController).Assembly.GetName();
        return new ServiceInfoOutputDto(
            assemblyName.Name ?? "unknown",
            assemblyName.Version?.ToString() ?? "unknown",
            DateTimeOffset.UtcNow);
    }

#if (LocalIdentity)
    /// <summary>
    /// 返回「本次调用以谁的身份进入」：用户（可能经服务调用头恢复）与调用方客户端。
    /// 默认授权策略要求可用的自然人用户——服务间调用须携带受信的 X-User-Id 才能通过；
    /// 面向纯工作负载（无用户上下文）的端点应单独声明自己的策略。
    /// </summary>
    [Authorize]
    [HttpGet("whoami")]
    public WhoAmIOutputDto WhoAmI(
        [FromServices] ICurrentUser currentUser,
        [FromServices] ICurrentClient currentClient) =>
        new(currentUser.Id, currentUser.Username, currentClient.ClientId);
#endif
}

/// <summary>
/// 服务基础信息。
/// </summary>
/// <param name="Service">服务名（程序集名）</param>
/// <param name="Version">程序集版本</param>
/// <param name="ServerTime">服务器当前时间（UTC）</param>
public sealed record ServiceInfoOutputDto(string Service, string Version, DateTimeOffset ServerTime);

#if (LocalIdentity)
/// <summary>
/// 当前调用身份。
/// </summary>
/// <param name="UserId">当前用户 Id（服务间调用时来自受信恢复的 X-User-Id）</param>
/// <param name="Username">当前用户名</param>
/// <param name="ClientId">调用方客户端 Id（client credentials 调用时存在）</param>
public sealed record WhoAmIOutputDto(Guid? UserId, string? Username, string? ClientId);
#endif
