#if (IncludeIdentity)
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Users;
using Microsoft.AspNetCore.Authorization;

namespace CompanyName.ProjectName.Api.Extensions;

/// <summary>
/// 要求当前主体是一个存在、启用且未锁定的自然人用户。
/// </summary>
/// <remarks>
/// 登录时会拒绝禁用与锁定账号，但已签发的 Cookie/Bearer 不会因此失效。若只在权限判定里补这道检查，
/// 撤权就只覆盖 RBAC 接口——`/auth/me`、通知这类"仅要求已认证"的端点仍然畅通，
/// 与"禁用用户"在界面上承诺的语义不符。因此把它挂在默认策略上，Cookie 与用户 Bearer 一起覆盖。
///
/// 每请求查库是可接受的：主体解析本来就要读用户与角色，这里复用同一次读取的位置，
/// 不需要引入安全戳、会话中心或分布式撤销。
///
/// **覆盖边界**：每一次新的 HTTP 请求、每一次新的 Hub 连接握手。**不包括**已经建立的
/// SignalR 连接——SignalR 只在握手阶段执行授权，连接建立后不会重跑策略，所以被禁用的用户
/// 仍可能在既有连接上继续收到推送。要让既有连接也断开，需要连接注册表加跨节点终止通道，
/// 那是有明确敏感度要求的业务项目自己的事，通用模板不预置。
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
    IRepository<User, Guid> userRepository) : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        // 没有用户身份的主体（client_credentials 的 sub 是 client_id，代表工作负载而非人）
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
        if (user == null || !user.IsActive || user.IsLockedOut())
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
