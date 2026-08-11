#if (IncludeIdentity)
namespace CompanyName.ProjectName.Client.Dtos;

/// <summary>
/// 当前调用身份（对应 <c>GET /api/v1/service-info/whoami</c>）。
/// </summary>
/// <param name="UserId">当前用户 Id（服务间调用时来自受信恢复的 X-User-Id）</param>
/// <param name="UserName">当前用户名</param>
/// <param name="ClientId">调用方客户端 Id</param>
public sealed record WhoAmIDto(Guid? UserId, string? UserName, string? ClientId);
#endif
