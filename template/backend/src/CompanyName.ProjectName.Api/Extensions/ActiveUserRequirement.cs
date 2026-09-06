#if (LocalIdentity)
using Leistd.Timing;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Users;
using Microsoft.AspNetCore.Authorization;

namespace CompanyName.ProjectName.Api.Extensions;

/// <summary>
/// 要求当前主体是一个存在、启用且未锁定的自然人用户。
/// </summary>
/// <remarks>
/// 默认策略在每个 HTTP 请求和每次 Hub 握手时重新确认账号状态，使禁用与锁定对
/// 已签发的 Cookie 和用户 Bearer 生效。已经建立的 SignalR 连接不会重新执行授权，
/// 因而不在本要求的撤权边界内。
/// </remarks>
public sealed class ActiveUserRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// 标记"凭据背后的账号已失效"这一种失败，供结果处理器把它映射成 401 而非 403。
    /// </summary>
    public const string InvalidAccountReason = "ActiveUser:InvalidAccount";
}

public sealed class ActiveUserHandler(
    ICurrentUser currentUser,
    IRepository<User, Guid> userRepository,
    IClock clock) : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        // 没有用户身份的主体（client_credentials 的 sub 形如 client:<client_id>，代表工作负载而非人）
        // 一律不满足本要求。默认策略是管理接口的兜底，它要表达的是"一个可用的自然人"，
        // 而不是"任何通过了认证的东西"——这两句话只在有用户时等价，恰恰在没有用户时分叉：
        // 不开角色时管理控制器只剩 [Authorize]，放行等于任何机器令牌都能列用户和 OAuth 客户端。
        // 确有面向工作负载的端点时，由该端点单独声明自己的策略，而不是把默认策略放宽。
        //
        // 这一支不打失效标记：机器令牌本身是有效的，只是没资格进人类管理端点——那是 403。
        if (currentUser.Id is not { } userId)
        {
            return;
        }

        var user = await userRepository.GetByIdAsync(userId);
        if (user == null || user.GetAccessStatus(clock.Now) != UserAccessStatus.Allowed)
        {
            // 账号已删除/禁用/锁定：这份 Cookie 或 Bearer 代表的身份已经不再成立，
            // 属于"凭据无效"而不是"权限不足"。打上标记，由 InvalidAccountResultHandler 返回 401。
            context.Fail(new AuthorizationFailureReason(
                this, ActiveUserRequirement.InvalidAccountReason));
            return;
        }

        context.Succeed(requirement);
    }
}
#endif
