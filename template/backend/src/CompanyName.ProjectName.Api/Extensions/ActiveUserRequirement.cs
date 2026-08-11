#if (IncludeIdentity)
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Users;
using Microsoft.AspNetCore.Authorization;

namespace CompanyName.ProjectName.Api.Extensions;

/// <summary>
/// 要求当前主体对应的用户存在、启用且未锁定。
/// </summary>
/// <remarks>
/// 登录时会拒绝禁用与锁定账号，但已签发的 Cookie/Bearer 不会因此失效。若只在权限判定里补这道检查，
/// 撤权就只覆盖 RBAC 接口——`/auth/me`、通知这类"仅要求已认证"的端点仍然畅通，
/// 与"禁用用户"在界面上承诺的语义不符。因此把它挂在默认策略上，Cookie 与用户 Bearer 一起覆盖。
///
/// 每请求查库是可接受的：主体解析本来就要读用户与角色，这里复用同一次读取的位置，
/// 不需要引入安全戳、会话中心或分布式撤销。
/// </remarks>
public sealed class ActiveUserRequirement : IAuthorizationRequirement;

public sealed class ActiveUserHandler(
    ICurrentUser currentUser,
    IRepository<User, Guid> userRepository) : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        // 没有用户身份的主体（client_credentials 代表的是工作负载而非人）不适用本要求，
        // 由端点自身的授权决定；在这里按"查不到用户"拒绝会把机器令牌一并挡掉。
        if (currentUser.Id is not { } userId)
        {
            context.Succeed(requirement);
            return;
        }

        var user = await userRepository.GetByIdAsync(userId);
        if (user == null || !user.IsActive || user.IsLockedOut())
        {
            return;
        }

        context.Succeed(requirement);
    }
}
#endif
